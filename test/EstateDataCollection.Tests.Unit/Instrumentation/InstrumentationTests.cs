using AwesomeAssertions;
using EstateDataCollection.Application;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Application.UseCases.Submissions.AutoSubmission;
using EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.Infrastructure.Persistence;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Infrastructure.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Instrumentation;

public class InstrumentationTests
{
    [Fact]
    public void When_Scanning_Application_Assembly_All_Instrumentation_Classes_Should_Be_Registered_And_Resolved()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();

        // Register fake logger instances for each instrumentation target
        services.AddTransient(typeof(ILogger<>), typeof(FakeLogger<>));

        var provider = services.BuildServiceProvider();

        // Act & Assert - Application Instrumentations
        provider.GetRequiredService<VerifySchemaUseCaseInstrumentation>().Should().NotBeNull();
        provider.GetRequiredService<SubmitSchemaUseCaseInstrumentation>().Should().NotBeNull();
        provider.GetRequiredService<CreateSubmissionUseCaseInstrumentation>().Should().NotBeNull();
        provider.GetRequiredService<UpdateSubmissionUseCaseInstrumentation>().Should().NotBeNull();
        provider.GetRequiredService<FinaliseSubmissionUseCaseInstrumentation>().Should().NotBeNull();
#pragma warning disable CS0612
        provider.GetRequiredService<AutoSubmissionUseCaseInstrumentation>().Should().NotBeNull();
#pragma warning restore CS0612
    }

    [Fact]
    public void When_Invoking_Instrumentation_Methods_It_Should_Forward_Structured_Logs_To_Logger()
    {
        // Arrange
        var mockLogger = Substitute.For<ILogger<VerifySchemaUseCase>>();
        mockLogger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var instrumentation = new VerifySchemaUseCaseInstrumentation(mockLogger);

        // Act
        instrumentation.VerifyingCandidateSchema(2, 1);
        instrumentation.SchemaVerificationSucceeded();

        // Assert
        mockLogger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Verifying candidate schema with 2 valid fixtures and 1 invalid fixtures")),
            null,
            Arg.Any<Func<object, Exception?, string>>()
        );

        mockLogger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Candidate schema syntax and test fixtures verified successfully")),
            null,
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    [Fact]
    public void When_Invoking_SubmitSchema_Instrumentation_It_Should_Forward_Logs()
    {
        // Arrange
        var mockLogger = Substitute.For<ILogger<SubmitSchemaUseCase>>();
        mockLogger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var instrumentation = new SubmitSchemaUseCaseInstrumentation(mockLogger);

        // Act
        instrumentation.SubmittingSchema("school-workforce", 1);
        instrumentation.SchemaSaved("school-workforce", 1, "abc123hash");

        // Assert
        mockLogger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Submitting schema contract school-workforce v1")),
            null,
            Arg.Any<Func<object, Exception?, string>>()
        );

        mockLogger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("abc123hash")),
            null,
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    [Fact]
    public void When_Invoking_Infrastructure_Instrumentations_It_Should_Forward_Logs()
    {
        // Arrange
        var mockRegLogger = Substitute.For<ILogger<BlobSchemaRegistry>>();
        mockRegLogger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var regInstrumentation = new BlobSchemaRegistryInstrumentation(mockRegLogger);

        var mockSubLogger = Substitute.For<ILogger<BlobSubmissionInfrastructure>>();
        mockSubLogger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var subInstrumentation = new BlobSubmissionInfrastructureInstrumentation(mockSubLogger);

        var mockValLogger = Substitute.For<ILogger<JsonSchemaNetValidator>>();
        mockValLogger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var valInstrumentation = new JsonSchemaNetValidatorInstrumentation(mockValLogger);

        var subId = Guid.NewGuid();

        // Act
        regInstrumentation.SchemaLoadedFromBlob("school-workforce", 1);
        subInstrumentation.DraftPayloadSaved(subId, "123456");
        valInstrumentation.ValidationPassed();

        // Assert
        mockRegLogger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("school-workforce v1 loaded from Blob")),
            null,
            Arg.Any<Func<object, Exception?, string>>()
        );

        mockSubLogger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("123456")),
            null,
            Arg.Any<Func<object, Exception?, string>>()
        );

        mockValLogger.Received(1).Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("validation passed")),
            null,
            Arg.Any<Func<object, Exception?, string>>()
        );
    }

    private sealed class FakeLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
