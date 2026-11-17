using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.AzureFunctions.AdminUi.Proxies;

public sealed class HttpRunSchemaTestsUseCaseProxy(IHttpClientFactory httpClientFactory)
    : AdminApiUseCaseProxyBase(httpClientFactory), IUseCase<RunSchemaTestsResult, RunSchemaTestsRequest>
{
    public Task<Result<RunSchemaTestsResult>> ExecuteAsync(
        RunSchemaTestsRequest request,
        CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<RunSchemaTestsRequest, RunSchemaTestsResult>(
            "schemas/tests/run",
            request,
            cancellationToken);
    }
}
