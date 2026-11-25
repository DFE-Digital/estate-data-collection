using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Abstraction;

public interface IIdentifierIndexRepository : IInfrastructure
{
    Task<Result> UpsertEntryAsync(
        string identifierType,
        string identifierValue,
        IdentifierIndexEntry entry,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<IdentifierIndexEntry>>> GetEntriesAsync(
        string identifierType,
        string identifierValue,
        CancellationToken cancellationToken = default);

    Task<Result<string?>> ReadSiblingPayloadAsync(
        IdentifierIndexEntry entry,
        CancellationToken cancellationToken = default);
}
