using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.UpdateSchemaTests;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.AzureFunctions.AdminUi.Proxies;

public sealed class HttpUpdateSchemaTestsUseCaseProxy(IHttpClientFactory httpClientFactory)
    : AdminApiUseCaseProxyBase(httpClientFactory), IUseCase<UpdateSchemaTestsResult, UpdateSchemaTestsRequest>
{
    private sealed record UpdateTestsPayload(
        IReadOnlyList<string> ValidFixtures,
        IReadOnlyList<string> InvalidFixtures);

    public Task<Result<UpdateSchemaTestsResult>> ExecuteAsync(
        UpdateSchemaTestsRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new UpdateTestsPayload(
            ValidFixtures: request.ValidFixtures ?? [],
            InvalidFixtures: request.InvalidFixtures ?? []);

        var uri = $"schemas/{Uri.EscapeDataString(request.SchemaName)}/versions/{request.Version}/tests";
        return PutJsonAsync<UpdateTestsPayload, UpdateSchemaTestsResult>(uri, payload, cancellationToken);
    }
}
