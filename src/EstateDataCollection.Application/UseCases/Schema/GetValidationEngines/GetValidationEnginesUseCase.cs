using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases.Schema.GetValidationEngines;

public sealed class GetValidationEnginesUseCase(
    IPayloadValidationService validationService
) : IUseCase<GetValidationEnginesResult, GetValidationEnginesRequest>
{
    public async Task<Result<GetValidationEnginesResult>> ExecuteAsync(
        GetValidationEnginesRequest request,
        CancellationToken cancellationToken = default)
    {
        var engines = validationService.GetRegisteredEngines();
        return Result<GetValidationEnginesResult>.Success(new GetValidationEnginesResult(engines));
    }
}
