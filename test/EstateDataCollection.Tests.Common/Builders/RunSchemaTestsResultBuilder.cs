using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class RunSchemaTestsResultBuilder
{
    private DateTimeOffset _executedAtUtc = DateTimeOffset.UtcNow;
    private string _appVersion = "1.0.0";
    private int? _totalSchemasChecked;
    private int? _passedCount;
    private int? _failedCount;
    private readonly List<SchemaTestSuiteItemResult> _results = [];

    public RunSchemaTestsResultBuilder ExecutedAt(DateTimeOffset executedAtUtc)
    {
        _executedAtUtc = executedAtUtc;
        return this;
    }

    public RunSchemaTestsResultBuilder WithAppVersion(string appVersion)
    {
        _appVersion = appVersion;
        return this;
    }

    public RunSchemaTestsResultBuilder WithCounts(int totalSchemasChecked, int passedCount, int failedCount)
    {
        _totalSchemasChecked = totalSchemasChecked;
        _passedCount = passedCount;
        _failedCount = failedCount;
        return this;
    }

    public RunSchemaTestsResultBuilder WithItem(SchemaTestSuiteItemResult item)
    {
        _results.Add(item);
        return this;
    }

    public RunSchemaTestsResultBuilder WithItem(
        string schemaName,
        int version = 1,
        string state = "Active",
        bool passed = true,
        bool hasFixtures = true,
        int validFixtureCount = 1,
        int invalidFixtureCount = 0,
        int businessRuleCount = 0,
        IReadOnlyList<string>? errors = null,
        string? identifierType = null,
        string? identifierPointer = null,
        IReadOnlyList<InvalidFixtureOutcome>? invalidFixtureOutcomes = null,
        IReadOnlyList<string>? warnings = null)
    {
        _results.Add(new SchemaTestSuiteItemResult(
            SchemaName: schemaName,
            Version: version,
            State: state,
            Passed: passed,
            HasFixtures: hasFixtures,
            ValidFixtureCount: validFixtureCount,
            InvalidFixtureCount: invalidFixtureCount,
            BusinessRuleCount: businessRuleCount,
            Errors: errors ?? [],
            IdentifierType: identifierType,
            IdentifierPointer: identifierPointer,
            InvalidFixtureOutcomes: invalidFixtureOutcomes,
            Warnings: warnings));
        return this;
    }

    public RunSchemaTestsResult Build()
    {
        var total = _totalSchemasChecked ?? _results.Count;
        var passed = _passedCount ?? _results.Count(r => r.Passed);
        var failed = _failedCount ?? _results.Count(r => !r.Passed);

        return new RunSchemaTestsResult(
            ExecutedAtUtc: _executedAtUtc,
            AppVersion: _appVersion,
            TotalSchemasChecked: total,
            PassedCount: passed,
            FailedCount: failed,
            Results: _results.ToArray());
    }
}
