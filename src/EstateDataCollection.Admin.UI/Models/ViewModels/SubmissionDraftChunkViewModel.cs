namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SubmissionDraftChunkViewModel(
    int Index,
    string BlobName,
    string ShortBlobName,
    string CreatedAtDisplay,
    string SizeDisplay,
    string FormattedJson);