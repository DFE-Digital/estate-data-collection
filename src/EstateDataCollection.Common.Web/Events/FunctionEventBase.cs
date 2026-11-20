using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Common.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Common.Web.Events;

/// <summary>
/// Shared execution pipeline for event-triggered Azure Function handlers.
/// Standardises trigger logging, payload binding, use-case dispatch, and completion/failure telemetry.
/// </summary>
public abstract class FunctionEventBase(
    FunctionEventInstrumentation? instrumentation = null
) : IFunctionEvent
{
    private readonly FunctionEventInstrumentation _instrumentation =
        instrumentation ?? new FunctionEventInstrumentation(NullLogger<FunctionEventInstrumentation>.Instance);

    protected async Task<Result<TResult>> ExecuteEventAsync<TRequest, TResult>(
        string eventName,
        string triggerSource,
        IUseCase<TResult, TRequest> useCase,
        Func<Task<Result<TRequest>>> bindEventAsync,
        CancellationToken cancellationToken = default)
    {
        _instrumentation.EventTriggered(eventName, triggerSource);

        var bindResult = await bindEventAsync();
        if (bindResult.IsFailure)
        {
            _instrumentation.EventFailed(
                eventName,
                triggerSource,
                bindResult.Error.Code,
                bindResult.Error.Description);
            return bindResult.Error;
        }

        var result = await useCase.ExecuteAsync(bindResult.Value, cancellationToken);
        if (result.IsFailure)
        {
            _instrumentation.EventFailed(
                eventName,
                triggerSource,
                result.Error.Code,
                result.Error.Description);
            return result.Error;
        }

        _instrumentation.EventCompleted(eventName, triggerSource);
        return result;
    }
}
