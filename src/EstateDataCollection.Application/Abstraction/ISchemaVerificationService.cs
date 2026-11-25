using System.Text.Json.Nodes;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Abstraction;

public interface ISchemaVerificationService : IApplication
{
    Task<Result<SchemaVerificationResult>> VerifyCandidateSchemaAsync(
        string? rawSchemaJson,
        string? rawRulesJson,
        IReadOnlyList<string> sampleValidPayloads,
        IReadOnlyList<string> sampleInvalidPayloads,
        SchemaIdentifierConfig? identifierConfig = null,
        CancellationToken cancellationToken = default,
        SchemaValidationPolicy? validationPolicy = null,
        IReadOnlyDictionary<string, JsonNode?>? ambientFixtures = null);
}
