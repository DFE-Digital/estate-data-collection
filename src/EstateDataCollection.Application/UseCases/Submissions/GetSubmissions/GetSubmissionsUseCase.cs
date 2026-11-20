using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissions;

public sealed class GetSubmissionsUseCase(
    ISchemaRegistry schemaRegistry,
    ISubmissionQueryRepository queryRepository
) : IUseCase<GetSubmissionsResult, GetSubmissionsRequest>
{
    public async Task<Result<GetSubmissionsResult>> ExecuteAsync(
        GetSubmissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemasResult = await schemaRegistry.ListSchemasAsync(null, cancellationToken);
        if (schemasResult.IsFailure)
        {
            return schemasResult.Error;
        }

        var submissionsResult = await queryRepository.ListSubmissionsAsync(
            schemaName: request.SchemaName,
            schemaVersion: request.SchemaVersion,
            status: request.Status,
            searchQuery: request.Query,
            cancellationToken: cancellationToken);

        if (submissionsResult.IsFailure)
        {
            return submissionsResult.Error;
        }

        var availableSchemas = schemasResult.Value
            .Select(m => m.Name)
            .Concat(submissionsResult.Value.Select(s => s.SchemaName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new GetSubmissionsResult(
            Submissions: submissionsResult.Value,
            AvailableSchemaNames: availableSchemas,
            SchemaName: request.SchemaName,
            SchemaVersion: request.SchemaVersion,
            Status: request.Status,
            Query: request.Query);
    }
}
