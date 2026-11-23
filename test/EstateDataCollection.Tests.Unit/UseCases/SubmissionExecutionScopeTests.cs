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

        var result = await service.ValidatePayloadWithTraceAsync("{}", resolved, isDraftUpdate: false, scope, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        scope.Processes.Should().HaveCount(2);
        scope.Processes[0].ProcessName.Should().Be("JsonSchemaValidator");
        scope.Processes[0].Outcome.Should().Be(SubmissionProcessOutcome.Succeeded);
        scope.Processes[1].ProcessName.Should().Be("QualityRulesValidator");
        scope.Processes[1].Outcome.Should().Be(SubmissionProcessOutcome.Failed);
        scope.Processes[1].Engine.Should().Be("cel");
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
            .Build();

        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        json.Should().Contain("\"outcome\":\"ValidationErrorsFound\"");

        var roundTripped = JsonSerializer.Deserialize<SubmissionProcessExecution>(
            json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true });

        roundTripped.Should().NotBeNull();
        roundTripped!.Outcome.Should().Be(SubmissionProcessOutcome.ValidationErrorsFound);
    }
}
