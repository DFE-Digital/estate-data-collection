namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissions;

public sealed record GetSubmissionsRequest(
    string? SchemaName = null,
    int? SchemaVersion = null,
    string? Status = null,
    string? Query = null);
