using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Validation;
using Json.Schema;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Registry;

public sealed partial class BlobSchemaRegistry(
    BlobServiceClient blobServiceClient,
    IOptions<BlobSchemaRegistryOptions> options,
    IMemoryCache cache,
    BlobSchemaRegistryInstrumentation? instrumentation = null,
    IBusinessLogicValidator? logicValidator = null,
    IOdcsContractParser? odcsParser = null,
    IOptions<ValidationOptions>? validationOptions = null) : ISchemaRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] IgnoredCompanionSuffixes =
    [
        ".error.json",
        ".valid.json",
        ".invalid.json",
        ".meta.json"
    ];

    private static readonly string[] KnownContractSuffixes =
    [
        ".odcs.yaml",
        ".odcs.yml",
        ".schema.json",
        ".rules.json",
        ".logic.json",
        ".yaml",
        ".yml",
        ".json"
    ];

    private readonly BlobSchemaRegistryInstrumentation _instrumentation =
        instrumentation ?? new BlobSchemaRegistryInstrumentation(NullLogger<BlobSchemaRegistry>.Instance);

    private readonly IBusinessLogicValidator _logicValidator =
        logicValidator ?? new JsonLogicValidator(
            [],
            validationOptions,
            new JsonLogicValidatorInstrumentation(NullLogger<JsonLogicValidator>.Instance));

    private readonly IOdcsContractParser _odcsParser =
        odcsParser ?? new OdcsContractParser();

    private readonly ValidationOptions _validationOptions =
        validationOptions?.Value ?? new ValidationOptions();

    public async Task<Result<ResolvedSchema>> ResolveActiveSchemaAsync(string schemaName, DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        var listResult = await ListSchemasAsync(schemaName, cancellationToken);
        if (listResult.IsFailure)
        {
            return listResult.Error;
        }

        if (listResult.Value.Count > 0)
        {
            var activeCandidate = listResult.Value
                .Where(m => m.IsActiveAt(timestamp))
                .OrderByDescending(m => m.Version)
                .FirstOrDefault();

            if (activeCandidate is not null)
            {
                return await GetExplicitSchemaAsync(schemaName, activeCandidate.Version, cancellationToken);
            }

            // If schemas exist for this schemaName, but none is active at the given timestamp, return SchemaNotActive
            return Error.Validation(ErrorCodes.SchemaNotActive, new { schemaName, Version = listResult.Value.Max(m => m.Version) });
        }

        // Fallback to explicit v1 lookup (which returns SchemaNotFound if v1 does not exist)
        var fallback = await GetExplicitSchemaAsync(schemaName, 1, cancellationToken);
        if (fallback.IsSuccess && !fallback.Value.Metadata.IsActiveAt(timestamp))
        {
            return Error.Validation(ErrorCodes.SchemaNotActive, new { schemaName, Version = 1 });
        }

        return fallback;
    }

    public async Task<Result<IReadOnlyList<SchemaMetadata>>> ListSchemasAsync(
        string? schemaNameFilter = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
            if (!await schemasContainer.ExistsAsync(cancellationToken))
            {
                return Array.Empty<SchemaMetadata>();
            }

            var prefix = string.IsNullOrWhiteSpace(schemaNameFilter) ? null : $"{schemaNameFilter.Trim().Trim('/')}/";
            var discoveredVersions = new Dictionary<(string Name, int Version), (bool HasSchema, bool HasMeta)>();

            await foreach (var item in schemasContainer.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
            {
                if (item.Name.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase))
                {
                    TryParseDraftBlobPathConvention(item.Name, out var sName, out var ver, out _);
                    if (!string.IsNullOrWhiteSpace(sName) && ver is > 0)
                    {
                        var key = (sName, ver.Value);
                        discoveredVersions.TryGetValue(key, out var existing);
                        discoveredVersions[key] = (true, existing.HasMeta);
                    }
                }
                else if (item.Name.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
                {
                    var withoutMeta = item.Name[..^".meta.json".Length] + ".schema.json";
                    TryParseDraftBlobPathConvention(withoutMeta, out var sName, out var ver, out _);
                    if (!string.IsNullOrWhiteSpace(sName) && ver is > 0)
                    {
                        var key = (sName, ver.Value);
                        discoveredVersions.TryGetValue(key, out var existing);
                        discoveredVersions[key] = (existing.HasSchema, true);
                    }
                }
            }

            var results = new List<SchemaMetadata>();
            foreach (var ((sName, ver), flags) in discoveredVersions.OrderBy(k => k.Key.Name).ThenBy(k => k.Key.Version))
            {
                if (!flags.HasSchema)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(schemaNameFilter) &&
                    !string.Equals(sName, schemaNameFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var explicitResult = await GetExplicitSchemaAsync(sName, ver, cancellationToken);
                if (explicitResult.IsSuccess)
                {
                    results.Add(explicitResult.Value.Metadata);
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            return Error.Failure(ErrorCodes.StorageReadFailed, new { ex.Message });
        }
    }

    public async Task<Result> UpdateSchemaMetadataAsync(
        SchemaMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var namespacing = new { schemaName = metadata.Name, version = metadata.Version };
        var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
        var metaBlob = schemasContainer.GetBlobClient(options.Value.MetaBlobDirectory.FormatWith(namespacing));

        string? previousMetaSnapshot = null;
        var updatedMetaUploaded = false;

        try
        {
            if (await metaBlob.ExistsAsync(cancellationToken))
            {
                previousMetaSnapshot = (await metaBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
            }

            var metaJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            using var metaStream = new MemoryStream(Encoding.UTF8.GetBytes(metaJson));
            await metaBlob.UploadAsync(metaStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }, cancellationToken);
            updatedMetaUploaded = true;

            cache.Remove(options.Value.CacheKey.FormatWith(namespacing));
            _instrumentation.SchemaSavedToBlob(metadata.Name, metadata.Version);

            return Result.Success();
        }
        catch (Exception ex)
        {
            if (updatedMetaUploaded && previousMetaSnapshot is not null)
            {
                try
                {
                    using var restoreStream = new MemoryStream(Encoding.UTF8.GetBytes(previousMetaSnapshot));
                    await metaBlob.UploadAsync(restoreStream, new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
                    }, CancellationToken.None);
                }
                catch (Exception rollbackEx)
                {
                    _instrumentation.SchemaSaveToBlobRollbackFailed(metadata.Name, metadata.Version, rollbackEx.Message);
                }
            }

            _instrumentation.SchemaSaveToBlobFailed(metadata.Name, metadata.Version, ex.Message);
            return Error.Failure(ErrorCodes.SchemaPersistFailed, new { ex.Message });
        }
    }

    public async Task<Result<ResolvedSchema>> GetExplicitSchemaAsync(string schemaName, int version, CancellationToken cancellationToken = default)
    {
        var namespacing = new { schemaName, version };
        var key = options.Value.CacheKey.FormatWith(namespacing);
        
        if (cache.TryGetValue(key, out ResolvedSchema? cached) && cached is not null)
        {
            _instrumentation.SchemaCacheHit(schemaName, version);
            return cached;
        }

        _instrumentation.SchemaCacheMiss(schemaName, version);
        
        var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
        
        var schemaBlob = schemasContainer.GetBlobClient(options.Value.SchemaBlobDirectory.FormatWith(namespacing));
        var metaBlob = schemasContainer.GetBlobClient(options.Value.MetaBlobDirectory.FormatWith(namespacing));

        if (!await schemaBlob.ExistsAsync(cancellationToken))
        {
            _instrumentation.SchemaBlobNotFound(schemaName, version);
            return Error.NotFound(ErrorCodes.SchemaNotFound, new { schemaName, version });
        }
        
        var schemaJson = (await schemaBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
        var compiledSchema = JsonSchema.FromText(schemaJson);

        SchemaMetadata metadata;
        if (await metaBlob.ExistsAsync(cancellationToken))
        {
            var metaJson = (await metaBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
            metadata = JsonSerializer.Deserialize<SchemaMetadata>(metaJson, JsonOptions)!;
        }
        else
        {
            // Never default to Active when no .meta.json sidecar exists;
            // keep the schema in Validated (unscheduled) state until explicitly scheduled via Admin API.
            metadata = SchemaMetadata.CreateDraft(schemaName, version, schemaJson);
            metadata.MarkValidated();
        }

        IReadOnlyList<BusinessRule>? rules = null;
        var rulesBlob = schemasContainer.GetBlobClient(options.Value.RulesBlobDirectory.FormatWith(namespacing));
        if (await rulesBlob.ExistsAsync(cancellationToken))
        {
            var rulesJson = (await rulesBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
            rules = JsonSerializer.Deserialize<List<BusinessRule>>(rulesJson, JsonOptions);
        }
        else if (!string.Equals(options.Value.RulesBlobDirectory, options.Value.LogicBlobDirectory, StringComparison.OrdinalIgnoreCase))
        {
            var logicBlob = schemasContainer.GetBlobClient(options.Value.LogicBlobDirectory.FormatWith(namespacing));
            if (await logicBlob.ExistsAsync(cancellationToken))
            {
                var logicJson = (await logicBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
                rules = JsonSerializer.Deserialize<List<BusinessRule>>(logicJson, JsonOptions);
            }
        }
        
        var resolved = new ResolvedSchema(metadata, compiledSchema, rules);
        cache.Set(key, resolved, TimeSpan.FromMinutes(15));
        _instrumentation.SchemaLoadedFromBlob(schemaName, version);

        return resolved;
    }

    public async Task<Result<SchemaVerificationResult>> VerifyCanditateSchemaAsync(
        string? rawSchemaJson,
        string? rawRulesJson,
        IReadOnlyList<string> sampleValidPayloads,
        IReadOnlyList<string> sampleInvalidPayloads,
        CancellationToken cancellationToken = default)
    {
        var syntaxErrors = new List<ValidationMessage>();
        var fixtureErrors = new List<ValidationMessage>();

        JsonSchema? candidateSchema = null;
        IReadOnlyList<BusinessRule>? candidateRules = null;

        if (string.IsNullOrWhiteSpace(rawSchemaJson) && string.IsNullOrWhiteSpace(rawRulesJson))
        {
            return new SchemaVerificationResult(
                [new ValidationMessage(typeof(BlobSchemaRegistry), "schema", "Either Schema JSON/YAML or Rules JSON must be provided.")],
                []);
        }

        // Route Detection: If rawSchemaJson is an ODCS YAML contract, parse it into Schema JSON + Rules JSON
        if (!string.IsNullOrWhiteSpace(rawSchemaJson) && _odcsParser.IsOdcsYaml(rawSchemaJson))
        {
            var odcsResult = _odcsParser.Parse(rawSchemaJson);
            if (odcsResult.IsFailure)
            {
                var msg = odcsResult.Error.Description ?? "Failed to parse ODCS YAML contract.";
                _instrumentation.CandidateVerificationCompleted(false, 1, 0);
                return new SchemaVerificationResult(
                    [new ValidationMessage(_odcsParser.GetType(), "odcs", msg)],
                    []);
            }

            rawSchemaJson = odcsResult.Value.GeneratedSchemaJson;
            if (string.IsNullOrWhiteSpace(rawRulesJson) && !string.IsNullOrWhiteSpace(odcsResult.Value.GeneratedRulesJson))
            {
                rawRulesJson = odcsResult.Value.GeneratedRulesJson;
            }
        }

        // Phase One A: Syntax & Meta-Schema Evaluation (Draft 2020-12 & Draft-07)
        if (!string.IsNullOrWhiteSpace(rawSchemaJson))
        {
            try
            {
                var node = JsonNode.Parse(rawSchemaJson);

                if (node is null)
                {
                    syntaxErrors.Add(new ValidationMessage(typeof(JsonSchema), "schema", "Schema JSON was empty or null."));
                }
                else
                {
                    var meta = ResolveMetaSchema(node, syntaxErrors);
                    if (meta is not null)
                    {
                        var metaResult = meta.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });

                        if (!metaResult.IsValid)
                        {
                            var errors = metaResult.Details
                                .Where(x => x.HasErrors)
                                .SelectMany(x => (x.Errors?.Values ?? []).Select(err => new ValidationMessage(
                                    typeof(JsonSchema),
                                    string.IsNullOrWhiteSpace(x.InstanceLocation.ToString()) ? "schema" : x.InstanceLocation.ToString(),
                                    err)))
                                .Distinct();

                            syntaxErrors.AddRange(errors);
                        }
                    }

                    candidateSchema = JsonSchema.FromText(rawSchemaJson);
                }
            }
            catch (Exception ex)
            {
                return new SchemaVerificationResult(
                    [
                        new ValidationMessage(typeof(JsonSchema), "schema", "Malformed JSON Schema syntax"),
                        new ValidationMessage(typeof(JsonSchema), "schema", ex.Message)
                    ],
                    []);
            }
        }

        // Phase One B: Syntax & Engine Validation for Quality Rules
        if (!string.IsNullOrWhiteSpace(rawRulesJson))
        {
            var logicSyntaxResult = _logicValidator.ValidateRulesSyntax(rawRulesJson, out candidateRules);
            if (logicSyntaxResult.IsSuccess && logicSyntaxResult.Value.Count > 0)
            {
                syntaxErrors.AddRange(logicSyntaxResult.Value);
            }
        }

        if (syntaxErrors.Count > 0)
        {
            _instrumentation.CandidateVerificationCompleted(false, syntaxErrors.Count, 0);
            return new SchemaVerificationResult(syntaxErrors, []);
        }
        
        // Phase Two: In-Flight Golden Fixture Dry-Runs (against both Schema and Quality Rules)
        var evalOptions = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = _validationOptions.RequireFormatValidation
        };

        // Ensure valid fixtures pass both schema and quality rules
        for (var i = 0; i < sampleValidPayloads.Count; i++)
        {
            var payload = sampleValidPayloads[i];
            var fixtureId = $"SampleValidFixtures:{i}";

            if (candidateSchema is not null)
            {
                try
                {
                    var validNode = JsonNode.Parse(payload);
                    var eval = candidateSchema.Evaluate(validNode, evalOptions);

                    if (!eval.IsValid)
                    {
                        fixtureErrors.Add(new ValidationMessage(
                            typeof(JsonSchema),
                            fixtureId,
                            $"{fixtureId} was expected to pass, but was rejected by the schema."));

                        fixtureErrors.AddRange((eval.Details?.Where(x => x.HasErrors).Select(e => e.Errors!) ?? [])
                            .Select(e => new ValidationMessage(
                                typeof(JsonSchema),
                                fixtureId,
                                $"{fixtureId} {string.Join(", ", e.Values)}")));

                        fixtureErrors.AddRange((eval.Errors?.Values ?? [])
                            .Select(e => new ValidationMessage(
                                typeof(JsonSchema),
                                fixtureId,
                                $"{fixtureId} {e}")));
                    }
                }
                catch (Exception ex)
                {
                    fixtureErrors.Add(new ValidationMessage(
                        typeof(JsonSchema),
                        fixtureId,
                        $"{fixtureId} could not be parsed: {ex.Message}"));
                }
            }

            if (candidateRules is { Count: > 0 })
            {
                var logicResult = await _logicValidator.ValidateAsync(payload, candidateRules, cancellationToken);
                if (logicResult.IsFailure)
                {
                    fixtureErrors.Add(new ValidationMessage(
                        _logicValidator.GetType(),
                        fixtureId,
                        $"{fixtureId} quality rule evaluation failed: {logicResult.Error.Description}"));
                }
                else if (!logicResult.Value.IsValid)
                {
                    var ruleErrors = string.Join("; ", logicResult.Value.Errors!.Select(e => $"{e.PropertyPath}: {e.Message}"));
                    fixtureErrors.Add(new ValidationMessage(
                        _logicValidator.GetType(),
                        fixtureId,
                        $"{fixtureId} violated quality rules: {ruleErrors}"));
                }
            }
        }
        
        // Ensure invalid fixtures fail either schema or quality rules
        for (var i = 0; i < sampleInvalidPayloads.Count; i++)
        {
            var payload = sampleInvalidPayloads[i];
            var fixtureId = $"SampleInvalidFixtures:{i}";
            var schemaFailed = false;
            var rulesFailed = false;

            if (candidateSchema is not null)
            {
                try
                {
                    var invalidNode = JsonNode.Parse(payload);
                    var eval = candidateSchema.Evaluate(invalidNode, evalOptions);
                    if (!eval.IsValid)
                    {
                        schemaFailed = true;
                    }
                }
                catch (Exception ex)
                {
                    if (candidateRules is null)
                    {
                        fixtureErrors.Add(new ValidationMessage(
                            typeof(JsonSchema),
                            fixtureId,
                            $"{fixtureId} could not be parsed: {ex.Message}"));
                    }
                    schemaFailed = true;
                }
            }

            if (candidateRules is { Count: > 0 })
            {
                var logicResult = await _logicValidator.ValidateAsync(payload, candidateRules, cancellationToken);
                if (logicResult.IsFailure || !logicResult.Value.IsValid)
                {
                    rulesFailed = true;
                }
            }

            var anyValidatorConfigured = candidateSchema is not null || candidateRules is not null;
            if (anyValidatorConfigured && !schemaFailed && !rulesFailed)
            {
                fixtureErrors.Add(new ValidationMessage(
                    typeof(BlobSchemaRegistry),
                    fixtureId,
                    $"{fixtureId} was expected to fail, but passed the schema."));
            }
        }

        var isSuccess = syntaxErrors.Count == 0 && fixtureErrors.Count == 0;
        _instrumentation.CandidateVerificationCompleted(isSuccess, syntaxErrors.Count, fixtureErrors.Count);
        return new SchemaVerificationResult(syntaxErrors, fixtureErrors);
    }
    
    public async Task<Result<bool>> ExistsAsync(string schemaName, int version, CancellationToken cancellationToken = default)
    {
        var namespacing = new { schemaName, version };
        var key = options.Value.CacheKey.FormatWith(namespacing);

        if (cache.TryGetValue(key, out _))
        {
            return true;
        }

        var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
        var schemaBlob = schemasContainer.GetBlobClient(options.Value.SchemaBlobDirectory.FormatWith(namespacing));

        return (await schemaBlob.ExistsAsync(cancellationToken)).Value;
    }

    public Task<Result> SaveSchemaAsync(SchemaMetadata metadata, CancellationToken cancellationToken = default)
    {
        return SaveSchemaAsync(metadata, null, null, cancellationToken);
    }

    public async Task<Result> SaveSchemaAsync(
        SchemaMetadata metadata,
        string? rawRulesJson,
        string? rawOdcsYaml = null,
        CancellationToken cancellationToken = default)
    {
        var namespacing = new { schemaName = metadata.Name, version = metadata.Version };
        
        var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
        
        var schemaBlob = schemasContainer.GetBlobClient(options.Value.SchemaBlobDirectory.FormatWith(namespacing));
        var metaBlob = schemasContainer.GetBlobClient(options.Value.MetaBlobDirectory.FormatWith(namespacing));
        var rulesBlob = schemasContainer.GetBlobClient(options.Value.RulesBlobDirectory.FormatWith(namespacing));
        var odcsBlob = schemasContainer.GetBlobClient(options.Value.OdcsBlobDirectory.FormatWith(namespacing));
        
        Response<BlobContentInfo>? rawSchemaFileUpload = null;
        Response<BlobContentInfo>? companionMetafieldUpload = null;
        Response<BlobContentInfo>? companionRulesUpload = null;
        Response<BlobContentInfo>? odcsUpload = null;
        
        try
        {
            // 1. Upload companion metadata file first ({schemaName}/v{version}.meta.json)
            var metaJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            using var metaStream = new MemoryStream(Encoding.UTF8.GetBytes(metaJson));
            companionMetafieldUpload = await metaBlob.UploadAsync(metaStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }, cancellationToken);

            // 2. Upload companion rules file if present ({schemaName}/v{version}.rules.json)
            if (!string.IsNullOrWhiteSpace(rawRulesJson))
            {
                using var rulesStream = new MemoryStream(Encoding.UTF8.GetBytes(rawRulesJson));
                companionRulesUpload = await rulesBlob.UploadAsync(rulesStream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
                }, cancellationToken);
            }

            // 3. Upload raw ODCS YAML contract if present ({schemaName}/v{version}.odcs.yaml)
            if (!string.IsNullOrWhiteSpace(rawOdcsYaml))
            {
                using var odcsStream = new MemoryStream(Encoding.UTF8.GetBytes(rawOdcsYaml));
                odcsUpload = await odcsBlob.UploadAsync(odcsStream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/x-yaml" }
                }, cancellationToken);
            }

            // 4. Upload raw schema file LAST as the commit marker ({schemaName}/v{version}.schema.json)
            using var schemaStream = new MemoryStream(Encoding.UTF8.GetBytes(metadata.RawJsonSchema));
            rawSchemaFileUpload = await schemaBlob.UploadAsync(schemaStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/schema+json" }
            }, cancellationToken);

            // Invalidate or update local memory cache
            cache.Remove(options.Value.CacheKey.FormatWith(namespacing));
            _instrumentation.SchemaSavedToBlob(metadata.Name, metadata.Version);

            return Result.Success();
        }
        catch (Exception ex)
        {
            try
            {
                if (rawSchemaFileUpload?.HasValue == true)
                {
                    await schemaBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }

                if (companionMetafieldUpload?.HasValue == true)
                {
                    await metaBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }

                if (companionRulesUpload?.HasValue == true)
                {
                    await rulesBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }

                if (odcsUpload?.HasValue == true)
                {
                    await odcsBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }
            }
            catch (Exception rollbackException)
            {
                // Best-effort compensating rollback; preserve original exception for logging and Result.Failure
                _instrumentation.SchemaSaveToBlobRollbackFailed(metadata.Name, metadata.Version, rollbackException.Message);
            }

            _instrumentation.SchemaSaveToBlobFailed(metadata.Name, metadata.Version, ex.Message);
            return Error.Failure(ErrorCodes.SchemaPersistFailed, new { ex.Message });
        }
    }

    public async Task<Result<DraftSchemaArtifact>> GetDraftArtifactAsync(
        string blobPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizeDraftBlobPath(blobPath, options.Value.DraftContainerName);
        TryParseDraftBlobPathConvention(normalizedPath, out var pathSchemaName, out var pathVersion, out var basePrefix);

        if (IgnoredCompanionSuffixes.Any(s => normalizedPath.EndsWith(s, StringComparison.OrdinalIgnoreCase)) ||
            normalizedPath.EndsWith(options.Value.DraftErrorBlobSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return new DraftSchemaArtifact(
                TriggerBlobPath: normalizedPath,
                SchemaName: pathSchemaName,
                Version: pathVersion,
                RawSchemaJson: null,
                RawRulesJson: null,
                RawOdcsYaml: null,
                SampleValidFixtures: [],
                SampleInvalidFixtures: [],
                ConsumedDraftBlobPaths: [],
                IsIgnoredCompanionBlob: true);
        }

        var draftContainer = blobServiceClient.GetBlobContainerClient(options.Value.DraftContainerName);
        var triggerBlob = draftContainer.GetBlobClient(normalizedPath);

        if (!await triggerBlob.ExistsAsync(cancellationToken))
        {
            return Error.NotFound(ErrorCodes.SchemaNotFound, new { blobPath = normalizedPath });
        }

        var content = (await triggerBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
        var consumedBlobs = new List<string> { normalizedPath };

        string? rawSchemaJson = null;
        string? rawRulesJson = null;
        string? rawOdcsYaml = null;

        if (_odcsParser.IsOdcsYaml(content, normalizedPath))
        {
            rawOdcsYaml = content;
        }
        else if (IsRulesBlob(normalizedPath, content))
        {
            rawRulesJson = content;

            // Check if companion {basePrefix}.schema.json is also present in draft-schemas
            var companionSchemaPath = $"{basePrefix}.schema.json";
            var companionSchemaBlob = draftContainer.GetBlobClient(companionSchemaPath);
            if (await companionSchemaBlob.ExistsAsync(cancellationToken))
            {
                rawSchemaJson = (await companionSchemaBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
                consumedBlobs.Add(companionSchemaPath);
            }
        }
        else
        {
            rawSchemaJson = content;

            // Check if companion {basePrefix}.rules.json or {basePrefix}.logic.json is also present in draft-schemas
            foreach (var rulesSuffix in new[] { ".rules.json", ".logic.json" })
            {
                var companionRulesPath = $"{basePrefix}{rulesSuffix}";
                var companionRulesBlob = draftContainer.GetBlobClient(companionRulesPath);
                if (await companionRulesBlob.ExistsAsync(cancellationToken))
                {
                    rawRulesJson = (await companionRulesBlob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
                    consumedBlobs.Add(companionRulesPath);
                    break;
                }
            }
        }

        // Check for optional in-flight golden fixture files in draft-schemas: {basePrefix}.valid.json and {basePrefix}.invalid.json
        var validFixtures = await TryLoadDraftFixturesAsync(draftContainer, $"{basePrefix}.valid.json", consumedBlobs, cancellationToken);
        var invalidFixtures = await TryLoadDraftFixturesAsync(draftContainer, $"{basePrefix}.invalid.json", consumedBlobs, cancellationToken);

        return new DraftSchemaArtifact(
            TriggerBlobPath: normalizedPath,
            SchemaName: pathSchemaName,
            Version: pathVersion,
            RawSchemaJson: rawSchemaJson,
            RawRulesJson: rawRulesJson,
            RawOdcsYaml: rawOdcsYaml,
            SampleValidFixtures: validFixtures,
            SampleInvalidFixtures: invalidFixtures,
            ConsumedDraftBlobPaths: consumedBlobs,
            IsIgnoredCompanionBlob: false);
    }

    public async Task<Result> CompleteDraftIngestionAsync(
        DraftSchemaArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        var draftContainer = blobServiceClient.GetBlobContainerClient(options.Value.DraftContainerName);
        var deletedCount = 0;

        foreach (var path in artifact.ConsumedDraftBlobPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await draftContainer.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                await draftContainer.GetBlobClient($"{path}{options.Value.DraftErrorBlobSuffix}").DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                deletedCount++;
            }
            catch
            {
                // Best-effort cleanup of consumed draft files
            }
        }

        _instrumentation.DraftBlobsCleanedUp(deletedCount, options.Value.DraftContainerName, artifact.TriggerBlobPath);
        return Result.Success();
    }

    public async Task<Result> RecordDraftIngestionFailureAsync(
        string blobPath,
        string? schemaName,
        int? version,
        IReadOnlyList<string> errors,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizeDraftBlobPath(blobPath, options.Value.DraftContainerName);
        var errorBlobPath = $"{normalizedPath}{options.Value.DraftErrorBlobSuffix}";
        var draftContainer = blobServiceClient.GetBlobContainerClient(options.Value.DraftContainerName);
        var errorBlob = draftContainer.GetBlobClient(errorBlobPath);

        Response<BlobContentInfo>? errorBlobUpload = null;
        try
        {
            var diagnosticPayload = new
            {
                blobPath = normalizedPath,
                schemaName,
                version,
                failedAtUtc = DateTimeOffset.UtcNow,
                errors
            };

            var errorJson = JsonSerializer.Serialize(diagnosticPayload, new JsonSerializerOptions { WriteIndented = true });
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(errorJson));
            errorBlobUpload = await errorBlob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }, cancellationToken);

            _instrumentation.DraftFailureRecorded(errorBlobPath, errors.Count);
            return Result.Success();
        }
        catch (Exception ex)
        {
            try
            {
                if (errorBlobUpload?.HasValue == true)
                {
                    await errorBlob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
                }
            }
            catch
            {
                // Best-effort compensating rollback
            }

            return Error.Failure(ErrorCodes.SchemaPersistFailed, new { ex.Message });
        }
    }

    private static async Task<IReadOnlyList<string>> TryLoadDraftFixturesAsync(
        BlobContainerClient draftContainer,
        string fixtureBlobPath,
        List<string> consumedBlobs,
        CancellationToken cancellationToken)
    {
        var blob = draftContainer.GetBlobClient(fixtureBlobPath);
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return [];
        }

        var raw = (await blob.DownloadContentAsync(cancellationToken)).Value.Content.ToString();
        consumedBlobs.Add(fixtureBlobPath);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            var node = JsonNode.Parse(raw);
            if (node is JsonArray arr)
            {
                return arr
                    .Where(item => item is not null)
                    .Select(item => item is JsonValue v && v.TryGetValue<string>(out var s) ? s : item!.ToJsonString())
                    .ToList();
            }
        }
        catch
        {
            // Fall through and return raw payload so verification reports any syntax issues
        }

        return [raw];
    }

    private static bool IsRulesBlob(string blobPath, string content)
    {
        if (blobPath.EndsWith(".rules.json", StringComparison.OrdinalIgnoreCase) ||
            blobPath.EndsWith(".logic.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return content.TrimStart().StartsWith('[');
    }

    private static string NormalizeDraftBlobPath(string blobPath, string draftContainerName)
    {
        var normalized = blobPath.Trim().TrimStart('/');
        var containerPrefix = $"{draftContainerName}/";
        if (normalized.StartsWith(containerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[containerPrefix.Length..];
        }

        return normalized;
    }

    private static void TryParseDraftBlobPathConvention(
        string normalizedPath,
        out string? schemaName,
        out int? version,
        out string basePrefix)
    {
        schemaName = null;
        version = null;
        basePrefix = normalizedPath;

        foreach (var suffix in KnownContractSuffixes)
        {
            if (normalizedPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                basePrefix = normalizedPath[..^suffix.Length];
                break;
            }
        }

        // Pattern 1: {schemaName}/v{version}.{ext} or {schemaName}/{version}.{ext}
        var folderMatch = FolderVersionRegex().Match(basePrefix);
        if (folderMatch.Success)
        {
            schemaName = folderMatch.Groups[RegexName].Value;
            if (int.TryParse(folderMatch.Groups[RegexVersion].Value, out var v) && v > 0)
            {
                version = v;
            }
            return;
        }

        // Pattern 2: {schemaName}-v{version}.{ext} or {schemaName}_v{version}.{ext}
        var flatVersionMatch = FlatVersionRegex().Match(basePrefix);
        if (flatVersionMatch.Success)
        {
            schemaName = flatVersionMatch.Groups[RegexName].Value;
            if (int.TryParse(flatVersionMatch.Groups[RegexVersion].Value, out var v) && v > 0)
            {
                version = v;
            }
        }
    }

    private JsonSchema? ResolveMetaSchema(JsonNode schemaNode, List<ValidationMessage> syntaxErrors)
    {
        if (schemaNode is JsonObject obj && obj.TryGetPropertyValue("$schema", out var schemaUriNode) &&
            schemaUriNode is JsonValue val && val.TryGetValue<string>(out var schemaUri) &&
            !string.IsNullOrWhiteSpace(schemaUri))
        {
            if (_validationOptions.AllowedMetaSchemas.Count > 0 &&
                !_validationOptions.AllowedMetaSchemas.Any(allowed =>
                    string.Equals(allowed.TrimEnd('#'), schemaUri.TrimEnd('#'), StringComparison.OrdinalIgnoreCase)))
            {
                syntaxErrors.Add(new ValidationMessage(
                    typeof(JsonSchema),
                    "$schema",
                    $"Meta-schema '{schemaUri}' is not in the configured AllowedMetaSchemas list."));
                return null;
            }

            if (schemaUri.Contains("draft-07", StringComparison.OrdinalIgnoreCase))
            {
                return MetaSchemas.Draft7;
            }
        }

        return MetaSchemas.Draft202012;
    }

    public const string RegexName = "name";
    public const string RegexVersion = "ver";

    [GeneratedRegex($@"^(?<{RegexName}>[^/]+)/v?(?<{RegexVersion}>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FolderVersionRegex();

    [GeneratedRegex($@"^(?<{RegexName}>.+?)[-_]v(?<{RegexVersion}>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FlatVersionRegex();
}
