using EstateDataCollection.Common;

namespace EstateDataCollection.Application.UseCases.Schema.DeleteSchemaVersion;

public sealed record DeleteSchemaVersionResult(
    string SchemaName,
    int Version,
    Node<int> State,
    DateTimeOffset DeletedAtUtc,
    bool ErrorReportPreserved
);
