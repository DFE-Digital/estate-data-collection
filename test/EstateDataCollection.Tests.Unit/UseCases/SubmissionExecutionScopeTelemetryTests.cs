using System.Diagnostics;
using System.Diagnostics.Metrics;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Telemetry;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class SubmissionExecutionScopeTelemetryTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Record_Process_Emit_Activity_Metrics_And_Logs()
    {
        // Arrange
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
                    if (tag.Key == "process.name") proc = tag.Value?.ToString();
                    if (tag.Key == "process.outcome") outcome = tag.Value?.ToString();
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

        Result<string> stepResult;
        IReadOnlyList<SubmissionProcessExecution> recordedProcesses;

        // Act
        using (var scope = SubmissionExecutionScope.Start(
                   "CheckSubmissionStatus",
                   submissionId,
                   instrumentation,
                   auditStore))
        {
            stepResult = await scope.ExecuteAsync(
                SubmissionProcessCatalog.SchemaResolution,
                () => Task.FromResult(Result<string>.Success("edc_payload v1")),
                val => new ProcessStepDetails(Summary: val));

            scope.RecordSkipped(SubmissionProcessCatalog.QualityRulesValidator, itemCount: 2, summary: "Skipped");
            await scope.RecordAuditAsync("edc_payload", 1, "Succeeded", 200, message: "OK");
            recordedProcesses = scope.Processes.ToList();
        }

        // Stop listening before enumerating captured collections while other test classes run in parallel
        meterListener.Dispose();
        activityListener.Dispose();

        // Assert
        stepResult.IsSuccess.Should().BeTrue();
        recordedProcesses.Should().HaveCount(2);
        recordedProcesses[0].ProcessName.Should().Be("SchemaResolution");
        recordedProcesses[0].Engine.Should().Be("schema-registry");
        recordedProcesses[0].Outcome.Should().Be(SubmissionProcessOutcome.Succeeded);
        recordedProcesses[1].Outcome.Should().Be(SubmissionProcessOutcome.Skipped);

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
    public async Task ExecuteAsync_When_Operation_Fails_Should_Record_ErrorCode_And_Errors_On_Process()
    {
        // Arrange
        using var scope = SubmissionExecutionScope.Start("UpdateSubmission", Guid.NewGuid());
        var failure = Error.Validation(
            "Schema.NotFound",
            "Pinned schema version was not found.",
            [new ValidationError("$.schemaVersion", "Version 99 does not exist", "SCHEMA_MISSING")]);

        // Act
        var result = await scope.ExecuteAsync(
            SubmissionProcessCatalog.SchemaResolution,
            () => Task.FromResult(Result<string>.Failure(failure)));

        // Assert
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
    public async Task StepAsync_Without_Active_Scope_Should_Execute_Directly()
    {
        // Arrange
        SubmissionExecutionScope.Current.Should().BeNull();

        // Act
        var result = await SubmissionExecutionScope.StepAsync(
            SubmissionProcessCatalog.SchemaResolution,
            () => Task.FromResult(Result<string>.Success("unscoped")));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("unscoped");
    }

    [Fact]
    public async Task Start_Should_Nest_And_Restore_Previous_Ambient_Scope()
    {
        // Arrange
        SubmissionExecutionScope? currentDuringInner = null;
        SubmissionExecutionScope? currentAfterInner = null;
        int innerProcessCount = 0;
        int outerProcessCount = 0;

        // Act
        using var outer = SubmissionExecutionScope.Start("UpdateSubmission", Guid.NewGuid());
        var currentDuringOuter = SubmissionExecutionScope.Current;

        using (var inner = SubmissionExecutionScope.Start("ValidateDraftPayload", Guid.Empty))
        {
            currentDuringInner = SubmissionExecutionScope.Current;
            await SubmissionExecutionScope.StepAsync(
                SubmissionProcessCatalog.JsonSchemaValidator,
                () => Task.FromResult(Result<string>.Success("ok")));
            innerProcessCount = inner.Processes.Count;
        }

        currentAfterInner = SubmissionExecutionScope.Current;
        outerProcessCount = outer.Processes.Count;
        outer.Dispose();

        // Assert
        currentDuringOuter.Should().BeSameAs(outer);
        currentDuringInner.Should().NotBeNull();
        innerProcessCount.Should().Be(1);
        currentAfterInner.Should().BeSameAs(outer);
        outerProcessCount.Should().Be(0);
        SubmissionExecutionScope.Current.Should().BeNull();
    }
}
