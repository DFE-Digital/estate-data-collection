using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Validators;

/// <summary>
/// Shared runtime helper for extracting and validating the configured establishment identifier
/// from submission payloads according to the schema's <see cref="SchemaIdentifierConfig"/>,
/// schema-derived requiredness, and <see cref="ValidationOptions"/>.
/// </summary>
public static class PayloadIdentifierExtractor
{
    public static readonly WorkflowEngineDescriptor Descriptor = new(
        EngineId: "identifier-extractor",
        DisplayName: SchemaValidationMessages.IdentifierExtractorDisplayName,
        Kind: ValidationEngineKinds.IdentifierExtraction,
        Description: SchemaValidationMessages.IdentifierExtractorDescription);

    public sealed record ExtractionEvaluation(
        bool IsEnabled,
        bool IsValid,
        SubmissionIdentifier? Identifier,
        string PointerPath,
        Error? FailureError,
        ValidationError? ValidationError);

    public static ExtractionEvaluation Evaluate(
        string rawPayloadJson,
        ResolvedSchema schema,
        ValidationOptions options)
    {
        var identifierConfig = schema.Metadata.IdentifierConfig;
        if (identifierConfig?.IsDisabled == true)
        {
            return new ExtractionEvaluation(
                IsEnabled: false,
                IsValid: true,
                Identifier: null,
                PointerPath: string.Empty,
                FailureError: null,
                ValidationError: null);
        }

        var identifierType = !string.IsNullOrWhiteSpace(identifierConfig?.Type)
            ? identifierConfig.Type.Trim()
            : options.DefaultIdentifierType;

        var searchPaths = !string.IsNullOrWhiteSpace(identifierConfig?.JsonPointer)
            ? [identifierConfig.JsonPointer]
            : options.DefaultIdentifierJsonPaths;

        // Derive requiredness directly from the JSON Schema when present; fall back to metadata/options when stubbed
        var schemaDerivedRequired = IdentifierSchemaValidator.TryResolveRequiredFromSchema(
            schema.Metadata.RawJsonSchema,
            searchPaths);

        var isRequired = schemaDerivedRequired
                         ?? identifierConfig?.Required
                         ?? options.RequireIdentifierOnFinalise;

        var inspection = SubmissionIdentifier.InspectPayload(rawPayloadJson, searchPaths, identifierType);
        if (inspection.IsFailure)
        {
            var errMsg = inspection.Error.Description ?? SchemaValidationMessages.IdentifierExtractionFailed(identifierType);
            var fallbackPtr = FormatPointer(searchPaths.FirstOrDefault(), options.DefaultIdentifierPointerFallback);
            return new ExtractionEvaluation(
                IsEnabled: true,
                IsValid: false,
                Identifier: null,
                PointerPath: fallbackPtr,
                FailureError: inspection.Error,
                ValidationError: new ValidationError($"#{fallbackPtr}", errMsg));
        }

        var outcome = inspection.Value;
        var pointer = outcome.MatchedPath ?? FormatPointer(searchPaths.FirstOrDefault(), options.DefaultIdentifierPointerFallback);

        if (outcome.Status == IdentifierExtractionStatus.Found)
        {
            return new ExtractionEvaluation(
                IsEnabled: true,
                IsValid: true,
                Identifier: outcome.Identifier,
                PointerPath: pointer,
                FailureError: null,
                ValidationError: null);
        }

        if (outcome.Status == IdentifierExtractionStatus.NotPresent && !isRequired)
        {
            return new ExtractionEvaluation(
                IsEnabled: true,
                IsValid: true,
                Identifier: null,
                PointerPath: pointer,
                FailureError: null,
                ValidationError: null);
        }

        var message = outcome.ErrorMessage
                      ?? SchemaValidationMessages.RequiredIdentifierMissingAtPath(identifierType, pointer);

        var error = Error.Validation(ErrorCodes.PayloadInvalidIdentifier, new { Message = message });

        return new ExtractionEvaluation(
            IsEnabled: true,
            IsValid: false,
            Identifier: null,
            PointerPath: pointer,
            FailureError: error,
            ValidationError: new ValidationError($"#{pointer}", message));
    }

    internal static string NormalisePathForComparison(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();
        if (trimmed.StartsWith("$.", StringComparison.Ordinal))
        {
            return ("/" + trimmed[2..].Replace('.', '/')).TrimEnd('/');
        }

        if (trimmed.StartsWith('$'))
        {
            trimmed = trimmed.TrimStart('$');
        }
        else
        {
            trimmed = trimmed.TrimStart('#');
        }

        return trimmed.TrimEnd('/');
    }

    private static string FormatPointer(string? rawPath, string fallbackPointer)
    {
        return SchemaIdentifierConfig.TryParsePointerSegments(rawPath, out var segments)
            ? SchemaIdentifierConfig.ToJsonPointer(segments)
            : fallbackPointer;
    }
}
