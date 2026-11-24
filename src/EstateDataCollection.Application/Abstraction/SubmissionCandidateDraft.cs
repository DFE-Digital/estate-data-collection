namespace EstateDataCollection.Application.Abstraction;

public sealed record SubmissionCandidateDraft(
    string MergedPayloadJson,
    int DraftCountAfterSave);