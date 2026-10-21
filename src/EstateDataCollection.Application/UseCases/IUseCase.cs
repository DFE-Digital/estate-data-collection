using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases;

public interface IUseCase;

public interface IUseCase<TResult> : IUseCase
{
    Task<Result<TResult>> ExecuteAsync(CancellationToken cancellationToken = default);
}

public interface IUseCase<TResult, in TRequest> : IUseCase
{
    Task<Result<TResult>> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default);
}
