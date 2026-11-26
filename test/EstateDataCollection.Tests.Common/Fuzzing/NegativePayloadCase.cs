namespace EstateDataCollection.Tests.Common.Fuzzing;

public sealed record NegativePayloadCase(
    FuzzSchemaProfile Profile,
    NegativeMutationKind MutationKind,
    int Seed,
    string PayloadJson,
    bool IsMalformedJson,
    string Description);