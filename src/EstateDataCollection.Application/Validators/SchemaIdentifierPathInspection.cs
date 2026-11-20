namespace EstateDataCollection.Application.Validators;

public sealed record SchemaIdentifierPathInspection(
    bool Exists,
    bool IsScalarCompatible,
    bool IsRequiredInSchema,
    bool AllowsNull,
    string? NormalizedPointer,
    string? FailureReason = null);