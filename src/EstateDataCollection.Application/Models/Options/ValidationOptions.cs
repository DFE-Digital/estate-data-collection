using System.ComponentModel.DataAnnotations;
using EstateDataCollection.Common.Markers;

namespace EstateDataCollection.Application.Models.Options;

public sealed class ValidationOptions : IOption<ValidationOptions>
{
    public static string SectionName
    {
        get { return "Validation"; }
    }

    /// <summary>
    /// Quality rule engines enabled at runtime and during schema verification.
    /// Defaults to "jsonlogic", "cel", and "unique".
    /// </summary>
    [Required]
    public IReadOnlyList<string> EnabledEngines { get; init; } = ["jsonlogic", "cel", "unique"];

    /// <summary>
    /// Default engine used when a rule does not explicitly specify an "engine" field.
    /// </summary>
    [Required, MinLength(1)]
    public string DefaultEngine { get; init; } = "jsonlogic";

    /// <summary>
    /// Behavior when a quality rule specifies an unknown or disabled engine.
    /// Defaults to Fail so invalid/unsupported engines are rejected immediately.
    /// </summary>
    public UnknownEngineBehavior UnknownEngineBehavior { get; init; } = UnknownEngineBehavior.Fail;

    /// <summary>
    /// Whether quality rules (JsonLogic, CEL, unique) are evaluated during draft updates (PUT /submissions/{id}).
    /// </summary>
    public bool RunQualityRulesOnDraftUpdate { get; init; } = true;

    /// <summary>
    /// Whether establishment identifier extraction is checked during draft updates (PUT /submissions/{id})
    /// and surfaced in validation issues.
    /// </summary>
    public bool ValidateIdentifierOnDraftUpdate { get; init; } = true;

    /// <summary>
    /// Whether rules with Severity = "WARNING" are treated as blocking validation errors.
    /// </summary>
    public bool TreatWarningsAsErrors { get; init; } = false;

    /// <summary>
    /// Whether JSON Schema format annotations (e.g. email, date-time) are strictly validated.
    /// </summary>
    public bool RequireFormatValidation { get; init; } = true;

    /// <summary>
    /// Allowed JSON Schema $schema URIs during verification (supports both Draft 2020-12 and Draft-07 exported by datacontract-cli).
    /// </summary>
    [Required]
    public IReadOnlyList<string> AllowedMetaSchemas { get; init; } =
    [
        "https://json-schema.org/draft/2020-12/schema",
        "http://json-schema.org/draft/2020-12/schema",
        "http://json-schema.org/draft-07/schema#",
        "https://json-schema.org/draft-07/schema#",
        "http://json-schema.org/draft-07/schema",
        "https://json-schema.org/draft-07/schema"
    ];

    /// <summary>
    /// Whether an establishment identifier is required by default when a schema lacks an explicit IdentifierConfig
    /// and does not declare one as optional.
    /// </summary>
    public bool RequireIdentifierOnFinalise { get; init; } = true;

    /// <summary>
    /// Default identifier type/label used when a schema does not specify an explicit IdentifierConfig.Type.
    /// Defaults to "URN".
    /// </summary>
    [Required, MinLength(1)]
    public string DefaultIdentifierType { get; init; } = "URN";

    /// <summary>
    /// Ordered JSON paths searched to extract the identifier when a schema does not specify an explicit JsonPointer.
    /// Defaults to ["school.urn", "urn"].
    /// </summary>
    [Required]
    public IReadOnlyList<string> DefaultIdentifierJsonPaths { get; init; } = ["school.urn", "urn"];

    /// <summary>
    /// Fallback JSON Pointer reported in diagnostics when no explicit or matched identifier pointer is available.
    /// Defaults to "/urn".
    /// </summary>
    [Required, MinLength(1)]
    public string DefaultIdentifierPointerFallback { get; init; } = "/urn";

    /// <summary>
    /// Maximum comprehension/loop iterations allowed per CEL evaluation.
    /// </summary>
    public int CelMaxIterations { get; init; } = 10_000;

    /// <summary>
    /// Automatic epsilon tolerance applied to floating-point comparisons to absorb binary float rounding noise
    /// (e.g. 0.1 + 0.2 == 0.30000000000000004) without masking genuine multi-decimal mismatches like 0.001 == 0.
    /// </summary>
    public double FloatArtifactEpsilon { get; init; } = 1e-9;

    /// <summary>
    /// Optional global currency rounding tolerance override (e.g. 0.005m) for financial total equality checks.
    /// </summary>
    public decimal? CurrencyRoundingTolerance { get; init; }
}
