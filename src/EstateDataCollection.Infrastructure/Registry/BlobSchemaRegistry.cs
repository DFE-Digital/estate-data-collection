using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Contracts;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using Json.Schema;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Infrastructure.Registry;

public sealed class BlobSchemaRegistry(
    BlobServiceClient blobServiceClient,
    IOptions<BlobSchemaRegistryOptions> options,
    IMemoryCache cache,
    BlobSchemaRegistryInstrumentation? instrumentation = null) : ISchemaRegistry
{
    private readonly BlobSchemaRegistryInstrumentation _instrumentation =
        instrumentation ?? new BlobSchemaRegistryInstrumentation(NullLogger<BlobSchemaRegistry>.Instance);

    public async Task<Result<ResolvedSchema>> ResolveActiveSchemaAsync(string schemaName, DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        // todo: This will be replaced by a call to the schema registry based on if a schema is active by their effective dates
        return await GetExplicitSchemaAsync(schemaName, 1, cancellationToken);
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
            metadata = JsonSerializer.Deserialize<SchemaMetadata>(metaJson)!;
        }
        else
        {
            // Default active if no metadata file was uploaded alongside
            metadata = SchemaMetadata.CreateDraft(schemaName, version, schemaJson);
            metadata.ScheduleActivation(DateTimeOffset.MinValue);
        }
        
        var resolved = new ResolvedSchema(metadata, compiledSchema);
        cache.Set(key, resolved, TimeSpan.FromMinutes(15));
        _instrumentation.SchemaLoadedFromBlob(schemaName, version);

        return resolved;
    }

    public async Task<Result<SchemaVerificationResult>> VerifyCanditateSchemaAsync(string rawSchemaJson, IReadOnlyList<string> sampleValidPayloads,
        IReadOnlyList<string> sampleInvalidPayloads, CancellationToken cancellationToken = default)
    {
        var syntaxErrors = new List<string>();
        var fixtureErrors = new List<string>();

        JsonSchema candidateSchema;

        // Phase One
        // Syntax & Meta-Schema Evaluation (Draft 2020-12)
        
        try
        {
            var node = JsonNode.Parse(rawSchemaJson);

            if (node is null)
            {
                return new SchemaVerificationResult(["Schema JSON was empty or null."], []);
            }

            var meta = MetaSchemas.Draft202012;
            var metaResult = meta.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });

            if (!metaResult.IsValid)
            {
                var errors = metaResult.Details
                    .Where(x => x.HasErrors)
                    .SelectMany(x => x.Errors?.Values ?? [])
                    .Distinct();

                syntaxErrors.AddRange(errors);
            }

            candidateSchema = JsonSchema.FromText(rawSchemaJson);
        }
        catch (Exception ex)
        {
            return new SchemaVerificationResult(["Malformed JSON Schema syntax", ex.Message], []);
        }

        if (syntaxErrors.Count > 0)
        {
            _instrumentation.CandidateVerificationCompleted(false, syntaxErrors.Count, 0);
            return new SchemaVerificationResult(syntaxErrors, []);
        }
        
        // Phase Two
        // Golden Fixture Dry-Runs

        var evalOptions = new EvaluationOptions { OutputFormat = OutputFormat.List };

        // Ensure valid fixtures pass
        for (var i = 0; i < sampleValidPayloads.Count; i++)
        {
            try
            {
                var validNode = JsonNode.Parse(sampleValidPayloads[i]);
                var eval = candidateSchema.Evaluate(validNode, evalOptions);

                if (!eval.IsValid)
                {
                    fixtureErrors.Add($"SampleValidFixtures:{i} was expected to pass, but was rejected by the schema.");
                    fixtureErrors.AddRange((eval.Details?.Where(x => x.HasErrors).Select(e => e.Errors!) ?? []).Select(e => $"SampleValidFixtures:{i} {string.Join(", ", e.Values)}"));
                    fixtureErrors.AddRange((eval.Errors?.Values ?? []).Select(e => $"SampleValidFixtures:{i} {e}"));
                }
            }
            catch (Exception ex)
            {
                fixtureErrors.Add($"SampleValidFixtures:{i} could not be parsed: {ex.Message}");
            }
        }
        
        // Ensure invalid fixtures fail
        for (var i = 0; i < sampleInvalidPayloads.Count; i++)
        {
            try
            {
                var invalidNode = JsonNode.Parse(sampleInvalidPayloads[i]);
                var eval = candidateSchema.Evaluate(invalidNode, evalOptions);

                if (eval.IsValid)
                {
                    fixtureErrors.Add($"SampleInvalidFixtures:{i} was expected to fail, but passed the schema.");
                    fixtureErrors.AddRange((eval.Details?.Where(x => x.HasErrors).Select(e => e.Errors!) ?? []).Select(e => $"SampleInvalidFixtures:{i} {string.Join(", ", e.Values)}"));
                    fixtureErrors.AddRange((eval.Errors?.Values ?? []).Select(e => $"SampleInvalidFixtures:{i} {e}"));
                }
            }
            catch (Exception ex)
            {
                fixtureErrors.Add($"SampleInvalidFixtures:{i} could not be parsed: {ex.Message}");
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
    
    public async Task<Result> SaveSchemaAsync(SchemaMetadata metadata, CancellationToken cancellationToken = default)
    {
        try
        {
            var namespacing = new { schemaName = metadata.Name, version = metadata.Version };
            var schemasContainer = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
            
            // Upload raw schema file
            var schemaBlob = schemasContainer.GetBlobClient(options.Value.SchemaBlobDirectory.FormatWith(namespacing));
            using var schemaStream = new MemoryStream(Encoding.UTF8.GetBytes(metadata.RawJsonSchema));
            await schemaBlob.UploadAsync(schemaStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/schema+json" }
            }, cancellationToken);

            // Upload companion metadata file
            var metaBlob = schemasContainer.GetBlobClient(options.Value.MetaBlobDirectory.FormatWith(namespacing));
            var metaJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            
            using var metaStream = new MemoryStream(Encoding.UTF8.GetBytes(metaJson));
            await metaBlob.UploadAsync(metaStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }, cancellationToken);

            // Invalidate or update local memory cache
            cache.Remove(options.Value.CacheKey.FormatWith(namespacing));
            _instrumentation.SchemaSavedToBlob(metadata.Name, metadata.Version);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _instrumentation.SchemaSaveToBlobFailed(metadata.Name, metadata.Version, ex.Message);
            return Error.Failure(ErrorCodes.SchemaPersistFailed, new { ex.Message });
        }
    }
}
