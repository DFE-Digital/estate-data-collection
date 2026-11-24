using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class SchemaVersionSummaryBuilder
{
    private string _schemaName = "edc_payload";
    private int _version = 1;
    private Node<int> _state = SchemaLifecycleState.Active.ToNode();
    private DateTimeOffset? _effectiveFromUtc;
    private DateTimeOffset? _effectiveToUtc;
    private bool _isActiveNow = true;
    private bool _canAcceptTraffic = true;
    private string _sha256Hash = "abcdef1234567890";
    private DateTimeOffset _createdAtUtc = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private SchemaIdentifierConfig? _identifierConfig;
    private DateTimeOffset? _lastTestRunAtUtc;
    private string? _lastTestRunAppVersion;
    private bool? _lastTestRunPassed;
    private IReadOnlyList<string>? _lastTestRunWarnings;

    public SchemaVersionSummaryBuilder WithSchema(string schemaName, int version = 1)
    {
        _schemaName = schemaName;
        _version = version;
        return this;
    }

    public SchemaVersionSummaryBuilder WithVersion(int version)
    {
        _version = version;
        return this;
    }

    public SchemaVersionSummaryBuilder WithState(
        SchemaLifecycleState state,
        bool? isActiveNow = null,
        bool? canAcceptTraffic = null)
    {
        _state = state.ToNode();
        _isActiveNow = isActiveNow ?? state == SchemaLifecycleState.Active;
        _canAcceptTraffic = canAcceptTraffic ?? state is SchemaLifecycleState.Active or SchemaLifecycleState.Scheduled;
        return this;
    }

    public SchemaVersionSummaryBuilder WithState(
        Node<int> state,
        bool? isActiveNow = null,
        bool? canAcceptTraffic = null)
    {
        _state = state;
        if (isActiveNow.HasValue)
        {
            _isActiveNow = isActiveNow.Value;
        }

        if (canAcceptTraffic.HasValue)
        {
            _canAcceptTraffic = canAcceptTraffic.Value;
        }

        return this;
    }

    public SchemaVersionSummaryBuilder WithEffectiveWindow(DateTimeOffset? fromUtc, DateTimeOffset? toUtc = null)
    {
        _effectiveFromUtc = fromUtc;
        _effectiveToUtc = toUtc;
        return this;
    }

    public SchemaVersionSummaryBuilder ActiveNow(bool isActiveNow = true, bool canAcceptTraffic = true)
    {
        _isActiveNow = isActiveNow;
        _canAcceptTraffic = canAcceptTraffic;
        return this;
    }

    public SchemaVersionSummaryBuilder WithHash(string sha256Hash)
    {
        _sha256Hash = sha256Hash;
        return this;
    }

    public SchemaVersionSummaryBuilder CreatedAt(DateTimeOffset createdAtUtc)
    {
        _createdAtUtc = createdAtUtc;
        return this;
    }

    public SchemaVersionSummaryBuilder WithIdentifier(string type, string? jsonPointer = null, bool? required = null)
    {
        _identifierConfig = new SchemaIdentifierConfig
        {
            Type = type,
            JsonPointer = jsonPointer,
            Required = required
        };
        return this;
    }

    public SchemaVersionSummaryBuilder WithIdentifierConfig(SchemaIdentifierConfig? config)
    {
        _identifierConfig = config;
        return this;
    }

    public SchemaVersionSummaryBuilder WithLastTestRun(
        DateTimeOffset atUtc,
        string appVersion,
        bool passed,
        IReadOnlyList<string>? warnings = null)
    {
        _lastTestRunAtUtc = atUtc;
        _lastTestRunAppVersion = appVersion;
        _lastTestRunPassed = passed;
        _lastTestRunWarnings = warnings;
        return this;
    }

    public SchemaVersionSummary Build()
    {
        return new SchemaVersionSummary(
            SchemaName: _schemaName,
            Version: _version,
            State: _state,
            EffectiveFromUtc: _effectiveFromUtc,
            EffectiveToUtc: _effectiveToUtc,
            IsActiveNow: _isActiveNow,
            CanAcceptTraffic: _canAcceptTraffic,
            Sha256Hash: _sha256Hash,
            CreatedAtUtc: _createdAtUtc,
            IdentifierConfig: _identifierConfig,
            LastTestRunAtUtc: _lastTestRunAtUtc,
            LastTestRunAppVersion: _lastTestRunAppVersion,
            LastTestRunPassed: _lastTestRunPassed,
            LastTestRunWarnings: _lastTestRunWarnings);
    }
}
