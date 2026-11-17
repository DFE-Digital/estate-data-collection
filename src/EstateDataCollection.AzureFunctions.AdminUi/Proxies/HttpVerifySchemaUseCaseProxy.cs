using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.AzureFunctions.AdminUi.Proxies;

public sealed class HttpVerifySchemaUseCaseProxy(IHttpClientFactory httpClientFactory)
    : AdminApiUseCaseProxyBase(httpClientFactory), IUseCase<VerifySchemaResult, VerifySchemaRequest>
{
    private sealed record VerifyPayload(
        string? SchemaJson,
        IReadOnlyList<string> SampleValidFixtures,
        IReadOnlyList<string> SampleInvalidFixtures,
        string? RulesJson,
        string? OdcsYaml,
        string? FileName,
        SchemaIdentifierConfig? IdentifierConfig = null);

    public Task<Result<VerifySchemaResult>> ExecuteAsync(
        VerifySchemaRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new VerifyPayload(
            SchemaJson: request.SchemaJson,
            SampleValidFixtures: request.SampleValidFixtures,
            SampleInvalidFixtures: request.SampleInvalidFixtures,
            RulesJson: request.RulesJson,
            OdcsYaml: request.OdcsYaml,
            FileName: request.FileName,
            IdentifierConfig: request.IdentifierConfig);

        return PostJsonAsync<VerifyPayload, VerifySchemaResult>("schemas/verify", payload, cancellationToken);
    }
}
