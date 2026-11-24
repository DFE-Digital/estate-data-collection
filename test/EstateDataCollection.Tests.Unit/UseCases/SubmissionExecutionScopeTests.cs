using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Application.Validators.Schema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Telemetry;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Tests.Common.Builders;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class SubmissionExecutionScopeTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Record_Process_Emit_Activity_Metrics_And_Logs()
    {
        var submissionId = Guid.NewGuid();
        var submissionIdStr = submissionId.ToString();
        var mockLogger = Substitute.For<ILogger<ProcessExecutionInstrumentation>>();
        mockLogger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var instrumentation = new ProcessExecutionInstrumentation(mockLogger);
        var auditStore = Substitute.For<ISubmissionAuditStore>();
        auditStore.AppendAsync(Arg.Any<SubmissionAuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("audit/entry.json"));

        var capturedActivities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EstateDataCollectionTelemetry.ApplicationSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (string.Equals(activity.GetTagItem("submission.id")?.ToString(), submissionIdStr, StringComparison.Ordinal))
                {
                    lock (capturedActivities)
                    {
                        capturedActivities.Add(activity);
                    }
                }
            }
        };
        ActivitySource.AddActivityListener(activityListener);

        var recordedExecutions = new List<(long Count, string ProcessName, string Outcome)>();
        var recordedDurations = new List<double>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == EstateDataCollectionTelemetry.ApplicationMeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == "edc.submission.process.executions")
            {
                string? proc = null;
                string? outcome = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "process.name")
                    {
                        proc = tag.Value?.ToString();
                    }

                    if (tag.Key == "process.outcome")
                    {
                        outcome = tag.Value?.ToString();
                    }
                }

                lock (recordedExecutions)
                {
                    recordedExecutions.Add((measurement, proc ?? string.Empty, outcome ?? string.Empty));
                }
            }
        });
        meterListener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "edc.submission.process.duration")
            {
                lock (recordedDurations)
                {
                    recordedDurations.Add(measurement);
                }
            }
        });
        meterListener.Start();

        using (var scope = SubmissionExecutionScope.Start(
                   "CheckSubmissionStatus",
                   submissionId,
                   instrumentation,
                   auditStore))
        {
            var res = await scope.ExecuteAsync(
                SubmissionProcessCatalog.SchemaResolution,
                () => Task.FromResult(Result<string>.Success("edc_payload v1")),
                val => new ProcessStepDetails(Summary: val));

            res.IsSuccess.Should().BeTrue();
            scope.RecordSkipped(SubmissionProcessCatalog.QualityRulesValidator, itemCount: 2, summary: "Skipped");

            await scope.RecordAuditAsync("edc_payload", 1, "Succeeded", 200, message: "OK");

            scope.Processes.Should().HaveCount(2);
            scope.Processes[0].ProcessName.Should().Be("SchemaResolution");
            scope.Processes[0].Engine.Should().Be("schema-registry");
            scope.Processes[0].Outcome.Should().Be(SubmissionProcessOutcome.Succeeded);
            scope.Processes[1].Outcome.Should().Be(SubmissionProcessOutcome.Skipped);
        }

        // Stop listening before enumerating captured collections while other test classes run in parallel
        meterListener.Dispose();
        activityListener.Dispose();

        capturedActivities.Select(a => a.OperationName)
            .Should().Contain(["SchemaResolution", "Submission:CheckSubmissionStatus"]);

        var stepActivity = capturedActivities.Single(a => a.OperationName == "SchemaResolution");
        stepActivity.GetTagItem("process.outcome").Should().Be("Succeeded");
        stepActivity.GetTagItem("submission.id").Should().Be(submissionIdStr);

        recordedExecutions.Should().Contain(x => x.ProcessName == "SchemaResolution" && x.Outcome == "Succeeded");
        recordedExecutions.Should().Contain(x => x.ProcessName == "QualityRulesValidator" && x.Outcome == "Skipped");
        recordedDurations.Should().HaveCountGreaterThanOrEqualTo(2);

        mockLogger.Received().Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("SchemaResolution") && o.ToString()!.Contains("succeeded")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task PayloadValidationService_Should_Preserve_Preceding_Steps_When_Later_Step_Fails()
    {
        var schemaValidator = Substitute.For<ISchemaValidator>();
        var logicValidator = Substitute.For<IBusinessLogicValidator>();

        var resolved = new ResolvedSchemaBuilder()
            .WithRules([new BusinessRule { Id = "RULE-1", Path = "urn", Description = "Must pass", Engine = "cel" }])
            .Build();

        schemaValidator.ValidateAsync("{}", resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult([])));
        logicValidator.ValidateAsync("{}", Arg.Any<IReadOnlyList<BusinessRule>>(), Arg.Any<CancellationToken>())
            .Returns(Result<BusinessRuleValidationResult>.Failure(Error.Failure(ErrorCodes.JsonMalformed, new { Message = "CEL engine failure" })));

        var service = new PayloadValidationService(schemaValidator, logicValidator);
        using var scope = SubmissionExecutionScope.Start("FinaliseSubmission", Guid.NewGuid());

        var result = await service.ValidatePayloadWithTraceAsync("{}", resolved, isDraftUpdate: false, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        scope.Processes.Should().HaveCount(2);
        scope.Processes[0].ProcessName.Should().Be("JsonSchemaValidator");
        scope.Processes[0].Outcome.Should().Be(SubmissionProcessOutcome.Succeeded);
        scope.Processes[1].ProcessName.Should().Be("QualityRulesValidator");
        scope.Processes[1].Outcome.Should().Be(SubmissionProcessOutcome.Failed);
        scope.Processes[1].Engine.Should().Be("cel");
    }

    [Fact]
    public async Task PayloadValidationService_Should_Attach_Per_Process_Errors_Only_To_Steps_With_Issues()
    {
        var schemaValidator = Substitute.For<ISchemaValidator>();
        var logicValidator = Substitute.For<IBusinessLogicValidator>();

        var resolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithUrnPointer("/urn", required: true))
            .WithRules([new BusinessRule { Id = "RULE-CAP", Path = "school_capacity", Description = "Capacity positive", Engine = "jsonlogic" }])
            .Build();

        schemaValidator.ValidateAsync(Arg.Any<string>(), resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult([])));
        logicValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<BusinessRule>>(), Arg.Any<CancellationToken>())
            .Returns(Result<BusinessRuleValidationResult>.Success(new BusinessRuleValidationResult(
                [new ValidationError("$.school_capacity", "Capacity must be > 0", "RULE-CAP")])));

        var service = new PayloadValidationService(schemaValidator, logicValidator);
        using var scope = SubmissionExecutionScope.Start("FinaliseSubmission", Guid.NewGuid());

        var result = await service.ValidatePayloadWithTraceAsync(
            """{"school_capacity":-5}""",
            resolved,
            isDraftUpdate: false,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        scope.Processes.Should().HaveCount(3);

        var schemaStep = scope.Processes[0];
        schemaStep.ProcessName.Should().Be("JsonSchemaValidator");
        schemaStep.Outcome.Should().Be(SubmissionProcessOutcome.Succeeded);
        schemaStep.ErrorCode.Should().BeNull();
        schemaStep.Errors.Should().BeNull();

        var ruleStep = scope.Processes[1];
        ruleStep.ProcessName.Should().Be("QualityRulesValidator");
        ruleStep.Outcome.Should().Be(SubmissionProcessOutcome.ValidationErrorsFound);
        ruleStep.Errors.Should().ContainSingle();
        ruleStep.Errors![0].PropertyPath.Should().Be("$.school_capacity");
        ruleStep.Errors[0].Message.Should().Be("Capacity must be > 0");
        ruleStep.Errors[0].RuleId.Should().Be("RULE-CAP");

        var idStep = scope.Processes[2];
        idStep.ProcessName.Should().Be("IdentifierExtractor");
        idStep.Outcome.Should().Be(SubmissionProcessOutcome.ValidationErrorsFound);
        idStep.ErrorCode.Should().Be("Payload.InvalidUrn");
        idStep.Errors.Should().ContainSingle();
        idStep.Errors![0].PropertyPath.Should().Be("#/urn");
        idStep.Errors[0].RuleId.Should().Be("Payload.InvalidUrn");
    }

    [Fact]
    public async Task ExecuteAsync_When_Operation_Fails_Should_Record_ErrorCode_And_Errors_On_Process()
    {
        using var scope = SubmissionExecutionScope.Start("UpdateSubmission", Guid.NewGuid());
        var failure = Error.Validation(
            "Schema.NotFound",
            "Pinned schema version was not found.",
            [new ValidationError("$.schemaVersion", "Version 99 does not exist", "SCHEMA_MISSING")]);

        var result = await scope.ExecuteAsync(
            SubmissionProcessCatalog.SchemaResolution,
            () => Task.FromResult(Result<string>.Failure(failure)));

        result.IsFailure.Should().BeTrue();
        var step = scope.Processes.Should().ContainSingle().Subject;
        step.Outcome.Should().Be(SubmissionProcessOutcome.Failed);
        step.ErrorCode.Should().Be("Schema.NotFound");
        step.Errors.Should().ContainSingle();
        step.Errors![0].PropertyPath.Should().Be("$.schemaVersion");
        step.Errors[0].Message.Should().Be("Version 99 does not exist");
        step.Errors[0].RuleId.Should().Be("SCHEMA_MISSING");
    }

    [Fact]
    public async Task Ambient_Current_And_StepAsync_Should_Restore_Previous_Scope_And_Work_Without_Active_Scope()
    {
        SubmissionExecutionScope.Current.Should().BeNull();

        var noScopeResult = await SubmissionExecutionScope.StepAsync(
            SubmissionProcessCatalog.SchemaResolution,
            () => Task.FromResult(Result<string>.Success("unscoped")));
        noScopeResult.IsSuccess.Should().BeTrue();
        noScopeResult.Value.Should().Be("unscoped");

        using (var outer = SubmissionExecutionScope.Start("UpdateSubmission", Guid.NewGuid()))
        {
            SubmissionExecutionScope.Current.Should().BeSameAs(outer);

            using (var inner = SubmissionExecutionScope.Start("ValidateDraftPayload", Guid.Empty))
            {
                SubmissionExecutionScope.Current.Should().BeSameAs(inner);
                await SubmissionExecutionScope.StepAsync(
                    SubmissionProcessCatalog.JsonSchemaValidator,
                    () => Task.FromResult(Result<string>.Success("ok")));
                inner.Processes.Should().ContainSingle();
            }

            SubmissionExecutionScope.Current.Should().BeSameAs(outer);
            outer.Processes.Should().BeEmpty();
        }

        SubmissionExecutionScope.Current.Should().BeNull();
    }

    [Fact]
    public void SubmissionProcessExecution_Should_Serialize_And_Deserialize_Outcome_As_PascalCase_String()
    {
        var entry = new SubmissionProcessExecutionBuilder()
            .For(SubmissionProcessCatalog.JsonSchemaValidator)
            .WithOutcome(SubmissionProcessOutcome.ValidationErrorsFound)
            .WithDurationMs(12)
            .WithItemCount(3)
            .WithSummary("3 schema error(s)")
            .WithErrorCode("Validation.Failed")
            .WithError("$.urn", "Missing URN", "RULE-URN")
            .Build();

        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        json.Should().Contain("\"outcome\":\"ValidationErrorsFound\"");
        json.Should().Contain("\"errorCode\":\"Validation.Failed\"");
        json.Should().Contain("\"propertyPath\":\"$.urn\"");

        var roundTripped = JsonSerializer.Deserialize<SubmissionProcessExecution>(
            json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true });

        roundTripped.Should().NotBeNull();
        roundTripped!.Outcome.Should().Be(SubmissionProcessOutcome.ValidationErrorsFound);
        roundTripped.ErrorCode.Should().Be("Validation.Failed");
        roundTripped.Errors.Should().ContainSingle();
        roundTripped.Errors![0].RuleId.Should().Be("RULE-URN");
    }
}
