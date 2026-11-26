using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class SchemaVersionDetailBuilder
{
    private string _schemaName = "edc_payload";
    private int _version = 1;
    private Node<int> _state = SchemaLifecycleState.Active.ToNode();
    private DateTimeOffset? _effectiveFromUtc = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private DateTimeOffset? _effectiveToUtc;
    private bool _isActiveNow = true;
    private bool _canAcceptTraffic = true;
    private string _sha256Hash = "deadbeef12345678";
    private DateTimeOffset _createdAtUtc = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly List<SchemaFieldDescriptor> _fields = [];
    private readonly List<SchemaQualityRuleSummary> _qualityRules = [];
    private string? _rawJsonSchema;
    private SchemaIdentifierConfig? _identifierConfig;
    private List<string>? _validFixtures;
    private List<string>? _invalidFixtures;
    private DateTimeOffset? _lastTestRunAtUtc;
    private string? _lastTestRunAppVersion;
    private bool? _lastTestRunPassed;
    private IReadOnlyList<string>? _lastTestRunErrors;
    private List<InvalidFixtureOutcome>? _lastTestRunInvalidFixtures;
    private List<string>? _lastTestRunWarnings;
    private List<ValidationEngineTrace>? _lastTestRunEngineTraces;
    private List<WorkflowEngineDescriptor>? _registeredEngines;

    public SchemaVersionDetailBuilder WithSchema(string schemaName, int version = 1)
    {
        _schemaName = schemaName;
        _version = version;
        return this;
    }

    public SchemaVersionDetailBuilder WithState(
        SchemaLifecycleState state,
        bool? isActiveNow = null,
        bool? canAcceptTraffic = null)
    {
        _state = state.ToNode();
        _isActiveNow = isActiveNow ?? state == SchemaLifecycleState.Active;
        _canAcceptTraffic = canAcceptTraffic ?? state is SchemaLifecycleState.Active or SchemaLifecycleState.Scheduled;
        if (state != SchemaLifecycleState.Active && state != SchemaLifecycleState.Scheduled)
        {
            _effectiveFromUtc = null;
        }

        return this;
    }

    public SchemaVersionDetailBuilder WithState(
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

    public SchemaVersionDetailBuilder WithEffectiveWindow(DateTimeOffset? fromUtc, DateTimeOffset? toUtc = null)
    {
        _effectiveFromUtc = fromUtc;
        _effectiveToUtc = toUtc;
        return this;
    }

    public SchemaVersionDetailBuilder ActiveNow(bool isActiveNow = true, bool canAcceptTraffic = true)
    {
        _isActiveNow = isActiveNow;
        _canAcceptTraffic = canAcceptTraffic;
        return this;
    }

    public SchemaVersionDetailBuilder WithHash(string sha256Hash)
    {
        _sha256Hash = sha256Hash;
        return this;
    }

    public SchemaVersionDetailBuilder CreatedAt(DateTimeOffset createdAtUtc)
    {
        _createdAtUtc = createdAtUtc;
        return this;
    }

    public SchemaVersionDetailBuilder WithField(SchemaFieldDescriptor field)
    {
        _fields.Add(field);
        return this;
    }

    public SchemaVersionDetailBuilder WithField(
        string name,
        string path,
        string type,
        bool required = false,
        SchemaFieldConstraints? constraints = null,
        params SchemaFieldDescriptor[] children)
    {
        _fields.Add(new SchemaFieldDescriptor(
            Name: name,
            Path: path,
            Type: type,
            Required: required,
            Constraints: constraints,
            Children: children.Length > 0 ? children : null));
        return this;
    }

    public SchemaVersionDetailBuilder WithQualityRule(SchemaQualityRuleSummary rule)
    {
        _qualityRules.Add(rule);
        return this;
    }

    public SchemaVersionDetailBuilder WithQualityRule(
        string id,
        string errorMessage,
        string? engine = "cel",
        string? dimension = "validity",
        string? severity = "error",
        string? targetPath = "",
        string? description = null)
    {
        _qualityRules.Add(new SchemaQualityRuleSummary(
            Id: id,
            Dimension: dimension,
            Severity: severity,
            TargetPath: targetPath,
            Engine: engine,
            ErrorMessage: errorMessage,
            Description: description));
        return this;
    }

    public SchemaVersionDetailBuilder WithRawJsonSchema(string? rawJsonSchema)
    {
        _rawJsonSchema = rawJsonSchema;
        return this;
    }

    public SchemaVersionDetailBuilder WithIdentifier(string type, string? jsonPointer = null, bool? required = null)
    {
        _identifierConfig = new SchemaIdentifierConfig
        {
            Type = type,
            JsonPointer = jsonPointer,
            Required = required
        };
        return this;
    }

    public SchemaVersionDetailBuilder WithIdentifierConfig(SchemaIdentifierConfig? config)
    {
        _identifierConfig = config;
        return this;
    }

    public SchemaVersionDetailBuilder WithValidFixtures(params string[] fixtures)
    {
        _validFixtures ??= [];
        _validFixtures.AddRange(fixtures);
        return this;
    }

    public SchemaVersionDetailBuilder WithInvalidFixtures(params string[] fixtures)
    {
        _invalidFixtures ??= [];
        _invalidFixtures.AddRange(fixtures);
        return this;
    }

    public SchemaVersionDetailBuilder WithLastTestRun(
        DateTimeOffset atUtc,
        string appVersion,
        bool passed,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? warnings = null)
    {
        _lastTestRunAtUtc = atUtc;
        _lastTestRunAppVersion = appVersion;
        _lastTestRunPassed = passed;
        _lastTestRunErrors = errors;
        if (warnings is not null)
        {
            _lastTestRunWarnings ??= [];
            _lastTestRunWarnings.AddRange(warnings);
        }

        return this;
    }

    public SchemaVersionDetailBuilder WithInvalidFixtureOutcome(InvalidFixtureOutcome outcome)
    {
        _lastTestRunInvalidFixtures ??= [];
        _lastTestRunInvalidFixtures.Add(outcome);
        return this;
    }

    public SchemaVersionDetailBuilder WithInvalidFixtureOutcome(
        int index,
        bool failedAsExpected,
        IReadOnlyList<ValidationMessage> triggeredErrors,
        IReadOnlyList<string>? expectedErrors = null,
        IReadOnlyList<string>? unmatchedExpectedErrors = null,
        string? description = null,
        string? warning = null)
    {
        _lastTestRunInvalidFixtures ??= [];
        _lastTestRunInvalidFixtures.Add(new InvalidFixtureOutcome(
            Index: index,
            FailedAsExpected: failedAsExpected,
            TriggeredErrors: triggeredErrors,
            ExpectedErrors: expectedErrors,
            UnmatchedExpectedErrors: unmatchedExpectedErrors,
            Description: description,
            Warning: warning));
        return this;
    }

    public SchemaVersionDetailBuilder WithLastTestRunWarnings(params string[] warnings)
    {
        _lastTestRunWarnings ??= [];
        _lastTestRunWarnings.AddRange(warnings);
        return this;
    }

    public SchemaVersionDetailBuilder WithEngineTrace(ValidationEngineTrace trace)
    {
        _lastTestRunEngineTraces ??= [];
        _lastTestRunEngineTraces.Add(trace);
        return this;
    }

    public SchemaVersionDetailBuilder WithEngineTrace(
        string engineId,
        string displayName,
        string kind,
        string outcome = "Succeeded",
        int itemsEvaluated = 1,
        int violationCount = 0,
        long durationMs = 1,
        string? summary = null,
        string? description = null)
    {
        _lastTestRunEngineTraces ??= [];
        _lastTestRunEngineTraces.Add(new ValidationEngineTrace(
            new WorkflowEngineDescriptor(
                engineId,
                displayName,
                kind,
                description ?? $"{displayName} engine"),
            outcome,
            itemsEvaluated,
            violationCount,
            durationMs,
            summary));
        return this;
    }

    public SchemaVersionDetailBuilder WithRegisteredEngine(
        string engineId,
        string displayName,
        string kind,
        string? description = null)
    {
        _registeredEngines ??= [];
        _registeredEngines.Add(new WorkflowEngineDescriptor(
            engineId,
            displayName,
            kind,
            description ?? $"{displayName} engine"));
        return this;
    }

    public SchemaVersionDetail Build()
    {
        return new SchemaVersionDetail(
            SchemaName: _schemaName,
            Version: _version,
            State: _state,
            EffectiveFromUtc: _effectiveFromUtc,
            EffectiveToUtc: _effectiveToUtc,
            IsActiveNow: _isActiveNow,
            CanAcceptTraffic: _canAcceptTraffic,
            Sha256Hash: _sha256Hash,
            CreatedAtUtc: _createdAtUtc,
            Fields: _fields.ToArray(),
            QualityRules: _qualityRules.ToArray(),
            RawJsonSchema: _rawJsonSchema,
            IdentifierConfig: _identifierConfig,
            ValidFixtures: _validFixtures?.ToArray(),
            InvalidFixtures: _invalidFixtures?.ToArray(),
            LastTestRunAtUtc: _lastTestRunAtUtc,
            LastTestRunAppVersion: _lastTestRunAppVersion,
            LastTestRunPassed: _lastTestRunPassed,
            LastTestRunErrors: _lastTestRunErrors,
            LastTestRunInvalidFixtures: _lastTestRunInvalidFixtures?.ToArray(),
            LastTestRunWarnings: _lastTestRunWarnings?.ToArray(),
            LastTestRunEngineTraces: _lastTestRunEngineTraces?.ToArray(),
            RegisteredEngines: _registeredEngines?.ToArray());
    }
}
