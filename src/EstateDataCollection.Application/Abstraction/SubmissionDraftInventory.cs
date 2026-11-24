using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Abstraction;

public sealed record SubmissionDraftInventory(
    IReadOnlyList<SubmissionDraftItem> Items,
    int Count,
    DateTimeOffset? LastDraftAtUtc);