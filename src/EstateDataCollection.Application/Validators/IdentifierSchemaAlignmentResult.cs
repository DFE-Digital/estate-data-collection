using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Validators;

public sealed record IdentifierSchemaAlignmentResult(
    bool IsValid,
    bool IsEnabled,
    SchemaIdentifierConfig? EffectiveConfig,
    IReadOnlyList<string> ResolvedPaths,
    bool IsRequiredInSchema,
    IReadOnlyList<ValidationMessage> Errors);
