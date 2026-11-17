using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.AzureFunctions.AdminUi.Proxies;

public sealed class HttpScheduleSchemaUseCaseProxy(IHttpClientFactory httpClientFactory)
    : AdminApiUseCaseProxyBase(httpClientFactory), IUseCase<ScheduleSchemaResult, ScheduleSchemaRequest>
{
    private sealed record SchedulePayload(
        DateTimeOffset? EffectiveFromUtc,
        DateTimeOffset? EffectiveToUtc,
        string? LifecycleAction,
        SchemaIdentifierConfig? IdentifierConfig);

    public Task<Result<ScheduleSchemaResult>> ExecuteAsync(
        ScheduleSchemaRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new SchedulePayload(
            EffectiveFromUtc: request.EffectiveFromUtc,
            EffectiveToUtc: request.EffectiveToUtc,
            LifecycleAction: request.LifecycleAction,
            IdentifierConfig: request.IdentifierConfig);

        var uri = $"schemas/{Uri.EscapeDataString(request.SchemaName)}/versions/{request.Version}/schedule";
        return PatchJsonAsync<SchedulePayload, ScheduleSchemaResult>(uri, payload, cancellationToken);
    }
}
