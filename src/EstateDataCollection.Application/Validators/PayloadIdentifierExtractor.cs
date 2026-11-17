using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Validators;

/// <summary>
/// Shared runtime helper for extracting and validating the configured school/establishment identifier
/// from submission payloads according to the schema's <see cref="SchemaIdentifierConfig"/>,
/// schema-derived requiredness, and <see cref="ValidationOptions"/>.
/// </summary>
public static class PayloadIdentifierExtractor
{
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
            : SchemaIdentifierConfig.DefaultUrnType;

        var searchPaths = !string.IsNullOrWhiteSpace(identifierConfig?.JsonPointer)
            ? [identifierConfig.JsonPointer]
            : options.UrnJsonPaths;

        // Derive requiredness directly from the JSON Schema when present; fall back to metadata/options when stubbed
        var schemaDerivedRequired = IdentifierSchemaValidator.TryResolveRequiredFromSchema(
            schema.Metadata.RawJsonSchema,
            searchPaths);

        var isRequired = schemaDerivedRequired
                         ?? identifierConfig?.Required
                         ?? options.RequireUrnOnFinalise;

        var inspection = SubmissionIdentifier.InspectPayload(rawPayloadJson, searchPaths, identifierType);
        if (inspection.IsFailure)
        {
            var errMsg = inspection.Error.Description ?? $"Failed to extract identifier '{identifierType}'.";
            var fallbackPtr = FormatPointer(searchPaths.FirstOrDefault());
            return new ExtractionEvaluation(
                IsEnabled: true,
                IsValid: false,
                Identifier: null,
                PointerPath: fallbackPtr,
                FailureError: inspection.Error,
                ValidationError: new ValidationError($"#{fallbackPtr}", errMsg));
        }

        var outcome = inspection.Value;
        var pointer = outcome.MatchedPath ?? FormatPointer(searchPaths.FirstOrDefault());

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
                      ?? $"Required identifier '{identifierType}' was missing or null at '{pointer}'.";

        var error = string.Equals(identifierType, SchemaIdentifierConfig.DefaultUrnType, StringComparison.OrdinalIgnoreCase)
            ? Error.Validation(ErrorCodes.PayloadInvalidUrn, message)
            : Error.Validation(ErrorCodes.PayloadInvalidIdentifier, new { Message = message });

        return new ExtractionEvaluation(
            IsEnabled: true,
            IsValid: false,
            Identifier: null,
            PointerPath: pointer,
            FailureError: error,
            ValidationError: new ValidationError($"#{pointer}", message));
    }

    private static string FormatPointer(string? rawPath) =>
        SchemaIdentifierConfig.TryParsePointerSegments(rawPath, out var segments)
            ? SchemaIdentifierConfig.ToJsonPointer(segments)
            : "/urn";
}
