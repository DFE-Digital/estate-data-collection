using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Models;

public sealed record ContractConsistencyReport(
    IReadOnlyList<ValidationError> Findings,
    IReadOnlyList<ValidationMessage> Errors,
    IReadOnlyList<ValidationMessage> Warnings);