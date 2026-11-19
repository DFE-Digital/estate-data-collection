using System.Diagnostics;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Application.Validators;

public sealed class PayloadValidationService(
    ISchemaValidator schemaValidator,
    IBusinessLogicValidator logicValidator,
    IOptions<ValidationOptions>? options = null
) : IPayloadValidationService
{
    private readonly ValidationOptions _options = options?.Value ?? new ValidationOptions();

    public async Task<Result<IReadOnlyList<ValidationError>>> ValidatePayloadAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate = false,
        CancellationToken cancellationToken = default)
    {
        var report = await ValidatePayloadWithTraceAsync(rawPayloadJson, schema, isDraftUpdate, cancellationToken);
        return report.IsFailure ? report.Error : Result<IReadOnlyList<ValidationError>>.Success(report.Value.Errors);
    }

    public async Task<Result<PayloadValidationReport>> ValidatePayloadWithTraceAsync(
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate = false,
        CancellationToken cancellationToken = default)
    {
        var processes = new List<SubmissionProcessExecution>(3);
        var errors = new List<ValidationError>();

        // Step 1: Structural JSON Schema Validation
        var schemaSw = Stopwatch.StartNew();
        var schemaResult = await schemaValidator.ValidateAsync(rawPayloadJson, schema.Schema, cancellationToken);
        schemaSw.Stop();

        if (schemaResult.IsFailure)
        {
            processes.Add(new SubmissionProcessExecution
            {
                ProcessName = "JsonSchemaValidator",
                Engine = "json-schema",
                Outcome = "Failed",
                DurationMs = schemaSw.ElapsedMilliseconds,
                Summary = schemaResult.Error.Description ?? schemaResult.Error.Code
            });
            return schemaResult.Error;
        }

        var schemaErrorCount = schemaResult.Value.Errors?.Count ?? 0;
        if (schemaErrorCount > 0)
        {
            errors.AddRange(schemaResult.Value.Errors!);
        }

        processes.Add(new SubmissionProcessExecution
        {
            ProcessName = "JsonSchemaValidator",
            Engine = "json-schema",
            Outcome = schemaErrorCount == 0 ? "Succeeded" : "ValidationErrorsFound",
            DurationMs = schemaSw.ElapsedMilliseconds,
            ItemCount = schemaErrorCount,
            Summary = schemaErrorCount == 0 ? "Schema structure valid" : $"{schemaErrorCount} schema error(s)"
        });

        var shouldRunRules = !isDraftUpdate || _options.RunQualityRulesOnDraftUpdate;

        // Step 2: Quality / Business Rule Validation (when structural validation passed and rules exist)
        if (schema.Rules is { Count: > 0 })
        {
            var distinctEngines = schema.Rules
                .Select(r => r.ResolveEngine(_options.DefaultEngine))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var engineList = string.Join(", ", distinctEngines);

            if (!shouldRunRules)
            {
                processes.Add(new SubmissionProcessExecution
                {
                    ProcessName = "QualityRulesValidator",
                    Engine = engineList,
                    Outcome = "Skipped",
                    DurationMs = 0,
                    ItemCount = schema.Rules.Count,
                    Summary = "Skipped during draft update per ValidationOptions"
                });
            }
            else if (errors.Count > 0)
            {
                processes.Add(new SubmissionProcessExecution
                {
                    ProcessName = "QualityRulesValidator",
                    Engine = engineList,
                    Outcome = "Skipped",
                    DurationMs = 0,
                    ItemCount = schema.Rules.Count,
                    Summary = "Skipped because structural JSON Schema validation failed"
                });
            }
            else
            {
                var logicSw = Stopwatch.StartNew();
                var logicResult = await logicValidator.ValidateAsync(rawPayloadJson, schema.Rules, cancellationToken);
                logicSw.Stop();

                if (logicResult.IsFailure)
                {
                    processes.Add(new SubmissionProcessExecution
                    {
                        ProcessName = "QualityRulesValidator",
                        Engine = engineList,
                        Outcome = "Failed",
                        DurationMs = logicSw.ElapsedMilliseconds,
                        ItemCount = schema.Rules.Count,
                        Summary = logicResult.Error.Description ?? logicResult.Error.Code
                    });
                    return logicResult.Error;
                }

                var logicErrorCount = logicResult.Value.Errors?.Count ?? 0;
                if (logicErrorCount > 0)
                {
                    errors.AddRange(logicResult.Value.Errors!);
                }

                processes.Add(new SubmissionProcessExecution
                {
                    ProcessName = "QualityRulesValidator",
                    Engine = engineList,
                    Outcome = logicErrorCount == 0 ? "Succeeded" : "ValidationErrorsFound",
                    DurationMs = logicSw.ElapsedMilliseconds,
                    ItemCount = schema.Rules.Count,
                    Summary = logicErrorCount == 0
                        ? $"Evaluated {schema.Rules.Count} quality rule(s) with 0 failures"
                        : $"Evaluated {schema.Rules.Count} quality rule(s); {logicErrorCount} violation(s)"
                });
            }
        }

        // Step 3: School / Establishment Identifier Extraction Check
        var shouldCheckIdentifier = !isDraftUpdate || _options.ValidateIdentifierOnDraftUpdate;
        if (shouldCheckIdentifier)
        {
            var idSw = Stopwatch.StartNew();
            var identifierEval = PayloadIdentifierExtractor.Evaluate(rawPayloadJson, schema, _options);
            idSw.Stop();

            if (!identifierEval.IsValid && identifierEval.ValidationError is not null)
            {
                var normalizedIdentifierPath = NormalizePointerForDedupe(identifierEval.ValidationError.PropertyPath);
                var alreadyReportedOnSamePath = errors.Any(e =>
                    string.Equals(NormalizePointerForDedupe(e.PropertyPath), normalizedIdentifierPath, StringComparison.OrdinalIgnoreCase));

                if (!alreadyReportedOnSamePath)
                {
                    errors.Add(identifierEval.ValidationError);
                }

                processes.Add(new SubmissionProcessExecution
                {
                    ProcessName = "IdentifierExtractor",
                    Engine = "identifier-extractor",
                    Outcome = "ValidationErrorsFound",
                    DurationMs = idSw.ElapsedMilliseconds,
                    Summary = identifierEval.ValidationError.Message
                });
            }
            else
            {
                var idLabel = identifierEval.Identifier is { } id
                    ? $"{id.Type}={id.Value}"
                    : "Not required by schema";

                processes.Add(new SubmissionProcessExecution
                {
                    ProcessName = "IdentifierExtractor",
                    Engine = "identifier-extractor",
                    Outcome = "Succeeded",
                    DurationMs = idSw.ElapsedMilliseconds,
                    Summary = idLabel
                });
            }
        }

        return new PayloadValidationReport(errors, processes);
    }

    private static string NormalizePointerForDedupe(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().TrimStart('#').TrimEnd('/');
}
