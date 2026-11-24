namespace EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;

public sealed record RunSchemaTestsResult(
    DateTimeOffset ExecutedAtUtc,
    string AppVersion,
    int TotalSchemasChecked,
    int PassedCount,
    int FailedCount,
    IReadOnlyList<SchemaTestSuiteItemResult> Results);
