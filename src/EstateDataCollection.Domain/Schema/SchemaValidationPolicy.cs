using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Domain.Schema;

/// <summary>
/// Per-schema-version validation policy governing severity gating, optional workflow pipeline override,
/// cross-submission draft referencing behaviour, and numeric tolerance overrides.
/// </summary>
public sealed record SchemaValidationPolicy
{
    public static SchemaValidationPolicy Default { get; } = new();

    /// <summary>
    /// Minimum severity threshold considered blocking when computing IsSuccessful / IsReadyToFinalise.
    /// Defaults to Error (so Critical and Error block, while Warning and Info do not).
    /// Setting Warning escalates warnings to fail IsSuccessful.
    /// </summary>
    public RuleSeverity FailOnMinimumSeverity { get; init; } = RuleSeverity.Error;

    /// <summary>
    /// Optional coded workflow pipeline key override. Null resolves the global "Default" pipeline.
    /// </summary>
    public string? WorkflowPipelineKey { get; init; } = null;

    /// <summary>
    /// Controls how cross-submission lookups handle references to unfinalised Draft sibling submissions.
    /// </summary>
    public CrossSubmissionDraftPolicy CrossSubmissionDraftPolicy { get; init; } =
        CrossSubmissionDraftPolicy.WarnOnDraftReferences;

    /// <summary>
    /// Optional numeric comparison tolerance override for this schema version.
    /// </summary>
    public decimal? NumericToleranceOverride { get; init; }

    public SchemaValidationPolicy Normalise() => this with
    {
        WorkflowPipelineKey = string.IsNullOrWhiteSpace(WorkflowPipelineKey)
            ? null
            : WorkflowPipelineKey.Trim()
    };

    public Result ValidateDefinition()
    {
        if (!Enum.IsDefined(FailOnMinimumSeverity))
        {
            return Error.Validation(
                ErrorCodes.SchemaVerificationFailed,
                errors: [new ValidationError("validationPolicy.failOnMinimumSeverity", DomainMessages.UnsupportedFailOnMinimumSeverity(FailOnMinimumSeverity))]);
        }

        if (!Enum.IsDefined(CrossSubmissionDraftPolicy))
        {
            return Error.Validation(
                ErrorCodes.SchemaVerificationFailed,
                errors: [new ValidationError("validationPolicy.crossSubmissionDraftPolicy", DomainMessages.UnsupportedCrossSubmissionDraftPolicy(CrossSubmissionDraftPolicy))]);
        }

        if (NumericToleranceOverride.HasValue && NumericToleranceOverride.Value < 0m)
        {
            return Error.Validation(
                ErrorCodes.SchemaVerificationFailed,
                errors: [new ValidationError("validationPolicy.numericToleranceOverride", DomainMessages.NumericToleranceMustBeNonNegative)]);
        }

        return Result.Success();
    }
}
