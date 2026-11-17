using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.AzureFunctions.AdminUi.Proxies;

public sealed class HttpGetSchemasUseCaseProxy(IHttpClientFactory httpClientFactory)
    : AdminApiUseCaseProxyBase(httpClientFactory), IUseCase<GetSchemasResult, GetSchemasRequest>
{
    public Task<Result<GetSchemasResult>> ExecuteAsync(
        GetSchemasRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"includeUnscheduled={(request.IncludeUnscheduled ? "true" : "false")}",
            $"includeFields={(request.IncludeActiveVersionFields ? "true" : "false")}"
        };

        if (!string.IsNullOrWhiteSpace(request.SchemaName))
        {
            query.Add($"schemaName={Uri.EscapeDataString(request.SchemaName.Trim())}");
        }

        return GetAsync<GetSchemasResult>($"schemas?{string.Join("&", query)}", cancellationToken);
    }
}
