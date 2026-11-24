using System.Text.Json;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Web.Extensions;
using EstateDataCollection.Common.Web.Models.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Common.Web.Endpoints;

/// <summary>
/// Shared execution pipeline for HTTP-triggered Azure Function endpoints.
/// Standardises invocation logging, request binding, use-case dispatch,
/// Result-to-IActionResult mapping, and completion telemetry.
/// </summary>
public abstract class FunctionEndpointBase(
    FunctionEndpointInstrumentation? instrumentation = null
) : IFunctionEndpoint
{
    protected static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly FunctionEndpointInstrumentation _instrumentation =
        instrumentation ?? new FunctionEndpointInstrumentation(NullLogger<FunctionEndpointInstrumentation>.Instance);

    protected Task<IActionResult> ExecuteAsync<TRequest, TResult>(
        string endpointName,
        IUseCase<TResult, TRequest> useCase,
        Func<Task<Result<TRequest>>> bindRequestAsync,
        int successStatusCode = StatusCodes.Status200OK,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            endpointName,
            useCase,
            bindRequestAsync,
            static value => Result<TResult>.Success(value),
            successStatusCode,
            context,
            cancellationToken);
    }

    protected async Task<IActionResult> ExecuteAsync<TRequest, TUseCaseResult, TResponse>(
        string endpointName,
        IUseCase<TUseCaseResult, TRequest> useCase,
        Func<Task<Result<TRequest>>> bindRequestAsync,
        Func<TUseCaseResult, Result<TResponse>> mapResult,
        int successStatusCode = StatusCodes.Status200OK,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        _instrumentation.EndpointInvoked(endpointName, context ?? "default");

        Result<TRequest> bindResult;
        try
        {
            bindResult = await bindRequestAsync();
        }
        catch (RequestBodyTooLargeException ex)
        {
            _instrumentation.EndpointBindingFailed(endpointName, "Payload.TooLarge", ex.Message);
            var problem = HttpRequestExtensions.CreatePayloadTooLargeProblem(ex.MaxSizeBytes, PathString.Empty);
            _instrumentation.EndpointCompleted(endpointName, StatusCodes.Status413PayloadTooLarge);
            return new ObjectResult(ApiResponse<TResponse>.Failure(problem))
            {
                StatusCode = StatusCodes.Status413PayloadTooLarge
            };
        }

        if (bindResult.IsFailure)
        {
            _instrumentation.EndpointBindingFailed(
                endpointName,
                bindResult.Error.Code,
                bindResult.Error.Description);

            var bindErrorResult = Result<TResponse>.Failure(bindResult.Error).ToActionResult(successStatusCode);
            _instrumentation.EndpointCompleted(
                endpointName,
                (bindErrorResult as ObjectResult)?.StatusCode ?? StatusCodes.Status400BadRequest);
            return bindErrorResult;
        }

        var result = await useCase.ExecuteAsync(bindResult.Value, cancellationToken);
        var mapped = result.IsFailure
            ? Result<TResponse>.Failure(result.Error)
            : mapResult(result.Value);

        var actionResult = mapped.ToActionResult(successStatusCode);
        _instrumentation.EndpointCompleted(
            endpointName,
            (actionResult as ObjectResult)?.StatusCode ?? successStatusCode);

        return actionResult;
    }

    protected async Task<IActionResult> ExecuteAsync<TResponse>(
        string endpointName,
        Func<CancellationToken, Task<Result<TResponse>>> handlerAsync,
        int successStatusCode = StatusCodes.Status200OK,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        _instrumentation.EndpointInvoked(endpointName, context ?? "default");

        var result = await handlerAsync(cancellationToken);
        var actionResult = result.ToActionResult(successStatusCode);
        _instrumentation.EndpointCompleted(
            endpointName,
            (actionResult as ObjectResult)?.StatusCode ?? successStatusCode);

        return actionResult;
    }

    protected static Result<TDto> DeserialiseJson<TDto>(
        string rawBody,
        string malformedMessage,
        JsonSerializerOptions? jsonOptions = null) where TDto : class
    {
        try
        {
            var dto = JsonSerializer.Deserialize<TDto>(rawBody, jsonOptions ?? DefaultJsonOptions);
            return dto is null
                ? Error.Failure(ErrorCodes.PayloadEmpty)
                : dto;
        }
        catch
        {
            return Error.Failure(ErrorCodes.JsonMalformed, new { Message = malformedMessage });
        }
    }
}
