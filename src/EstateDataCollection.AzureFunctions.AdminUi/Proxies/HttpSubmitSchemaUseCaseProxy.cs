using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.AzureFunctions.AdminUi.Proxies;

public sealed class HttpSubmitSchemaUseCaseProxy(IHttpClientFactory httpClientFactory)
    : AdminApiUseCaseProxyBase(httpClientFactory), IUseCase<SubmitSchemaResult, SubmitSchemaRequest>
{
    private sealed record SubmitPayload(
        string SchemaJson,
        IReadOnlyList<string>? SampleValidFixtures,
        IReadOnlyList<string>? SampleInvalidFixtures,
        string? RulesJson,
        string? OdcsYaml,
        string? FileName,
        SchemaIdentifierConfig? IdentifierConfig);

    public Task<Result<SubmitSchemaResult>> ExecuteAsync(
        SubmitSchemaRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new SubmitPayload(
            SchemaJson: request.SchemaJson,
            SampleValidFixtures: request.SampleValidFixtures,
            SampleInvalidFixtures: request.SampleInvalidFixtures,
            RulesJson: request.RulesJson,
            OdcsYaml: request.OdcsYaml,
            FileName: request.FileName,
            IdentifierConfig: request.IdentifierConfig);

        var uri = $"schemas/{Uri.EscapeDataString(request.SchemaName)}/versions/{request.Version}";
        return PostJsonAsync<SubmitPayload, SubmitSchemaResult>(uri, payload, cancellationToken);
    }
}
