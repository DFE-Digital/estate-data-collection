using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.AzureFunctions.AdminUi.Proxies;

public sealed class HttpGetSchemaDetailUseCaseProxy(IHttpClientFactory httpClientFactory)
    : AdminApiUseCaseProxyBase(httpClientFactory), IUseCase<SchemaVersionDetail, GetSchemaDetailRequest>
{
    public Task<Result<SchemaVersionDetail>> ExecuteAsync(
        GetSchemaDetailRequest request,
        CancellationToken cancellationToken = default)
    {
        var uri = $"schemas/{Uri.EscapeDataString(request.SchemaName)}/versions/{request.Version}" +
                  $"?includeUnscheduled={(request.IncludeUnscheduled ? "true" : "false")}" +
                  $"&includeRawJsonSchema={(request.IncludeRawJsonSchema ? "true" : "false")}";

        return GetAsync<SchemaVersionDetail>(uri, cancellationToken);
    }
}
