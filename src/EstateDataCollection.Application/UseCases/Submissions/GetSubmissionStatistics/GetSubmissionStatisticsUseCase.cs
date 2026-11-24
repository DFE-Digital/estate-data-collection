using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatistics;

public sealed class GetSubmissionStatisticsUseCase(
    ISchemaRegistry schemaRegistry,
    ISubmissionQueryRepository queryRepository
) : IUseCase<GetSubmissionStatisticsResult, GetSubmissionStatisticsRequest>
{
    public async Task<Result<GetSubmissionStatisticsResult>> ExecuteAsync(
        GetSubmissionStatisticsRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemasResult = await schemaRegistry.ListSchemasAsync(null, cancellationToken);
        if (schemasResult.IsFailure)
        {
            return schemasResult.Error;
        }

        var submissionsResult = await queryRepository.ListSubmissionsAsync(
            schemaName: request.SchemaName,
            cancellationToken: cancellationToken);
        if (submissionsResult.IsFailure)
        {
            return submissionsResult.Error;
        }

        var now = DateTimeOffset.UtcNow;
        var last24hCutoff = now.AddHours(-24);
        var last7dCutoff = now.AddDays(-7);

        var submissions = submissionsResult.Value;
        var allSchemas = schemasResult.Value;

        var schemaLookup = new Dictionary<(string SchemaName, int Version), string>(
            new SchemaVersionKeyComparer());

        foreach (var meta in allSchemas)
        {
            var effectiveState = meta.GetEffectiveStateAt(now).ToNode().Name;
            schemaLookup[(meta.Name, meta.Version)] = effectiveState;
        }

        var submissionsBySchemaVersion = submissions
            .GroupBy(s => (s.SchemaName, s.SchemaVersion), new SchemaVersionKeyComparer())
            .ToDictionary(g => g.Key, g => g.ToList(), new SchemaVersionKeyComparer());

        var statKeys = new HashSet<(string SchemaName, int Version)>(new SchemaVersionKeyComparer());
        foreach (var meta in allSchemas)
        {
            if (string.IsNullOrWhiteSpace(request.SchemaName) ||
                string.Equals(meta.Name, request.SchemaName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                statKeys.Add((meta.Name, meta.Version));
            }
        }

        foreach (var key in submissionsBySchemaVersion.Keys)
        {
            statKeys.Add(key);
        }

        var schemaStats = new List<SchemaSubmissionStatistic>(statKeys.Count);

        foreach (var key in statKeys)
        {
            submissionsBySchemaVersion.TryGetValue(key, out var groupItems);
            groupItems ??= [];

            var total = groupItems.Count;
            var finalised = groupItems.Count(s => s.IsFinalised);
            var drafts = total - finalised;
            var draftsWithPayload = groupItems.Count(s => !s.IsFinalised && s.HasDraftPayload);
            var emptyDrafts = drafts - draftsWithPayload;
            var chunks = groupItems.Sum(s => s.DraftCount);
            var audits = groupItems.Sum(s => s.AuditEventCount);
            var rate = total > 0 ? (int)Math.Round(finalised * 100.0 / total, MidpointRounding.AwayFromZero) : 0;
            DateTimeOffset? lastActivity = groupItems.Count > 0 ? groupItems.Max(s => s.LastActivityAtUtc) : null;

            var schemaState = schemaLookup.TryGetValue(key, out var foundState)
                ? foundState
                : "Unregistered";

            schemaStats.Add(new SchemaSubmissionStatistic(
                SchemaName: key.SchemaName,
                Version: key.Version,
                SchemaState: schemaState,
                TotalSubmissions: total,
                DraftSubmissions: drafts,
                DraftWithPayloadSubmissions: draftsWithPayload,
                EmptyDraftSubmissions: emptyDrafts,
                FinalisedSubmissions: finalised,
                TotalDraftChunks: chunks,
                TotalAuditEvents: audits,
                FinalisationRatePercent: rate,
                LastActivityAtUtc: lastActivity));
        }

        var orderedStats = schemaStats
            .OrderByDescending(s => s.TotalSubmissions)
            .ThenBy(s => s.SchemaName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(s => s.Version)
            .ToList();

        var totalAll = submissions.Count;
        var finalisedAll = submissions.Count(s => s.IsFinalised);
        var draftsAll = totalAll - finalisedAll;
        var draftsWithPayloadAll = submissions.Count(s => !s.IsFinalised && s.HasDraftPayload);
        var emptyDraftsAll = draftsAll - draftsWithPayloadAll;
        var rateAll = totalAll > 0 ? (int)Math.Round(finalisedAll * 100.0 / totalAll, MidpointRounding.AwayFromZero) : 0;

        var availableSchemas = allSchemas
            .Select(m => m.Name)
            .Concat(submissions.Select(s => s.SchemaName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new GetSubmissionStatisticsResult(
            TotalSubmissions: totalAll,
            DraftSubmissions: draftsAll,
            DraftWithPayloadSubmissions: draftsWithPayloadAll,
            EmptyDraftSubmissions: emptyDraftsAll,
            FinalisedSubmissions: finalisedAll,
            SubmissionsLast24Hours: submissions.Count(s => s.CreatedAtUtc >= last24hCutoff),
            SubmissionsLast7Days: submissions.Count(s => s.CreatedAtUtc >= last7dCutoff),
            TotalDraftChunks: submissions.Sum(s => s.DraftCount),
            TotalAuditEvents: submissions.Sum(s => s.AuditEventCount),
            FinalisationRatePercent: rateAll,
            SchemaStatistics: orderedStats,
            RecentSubmissions: submissions.Take(10).ToList(),
            AvailableSchemaNames: availableSchemas,
            FilteredSchemaName: request.SchemaName);
    }

    private sealed class SchemaVersionKeyComparer : IEqualityComparer<(string SchemaName, int Version)>
    {
        public bool Equals((string SchemaName, int Version) x, (string SchemaName, int Version) y)
        {
            return x.Version == y.Version &&
                   string.Equals(x.SchemaName, y.SchemaName, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string SchemaName, int Version) obj)
        {
            return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SchemaName), obj.Version);
        }
    }
}
