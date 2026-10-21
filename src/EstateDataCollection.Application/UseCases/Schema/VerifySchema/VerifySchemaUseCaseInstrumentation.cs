using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.UseCases.Schema.VerifySchema;

public sealed partial class VerifySchemaUseCaseInstrumentation(
    ILogger<VerifySchemaUseCase> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Verifying candidate schema with {ValidFixturesCount} valid fixtures and {InvalidFixturesCount} invalid fixtures"
    )]
    public partial void VerifyingCandidateSchema(int validFixturesCount, int invalidFixturesCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Candidate schema syntax and test fixtures verified successfully"
    )]
    public partial void SchemaVerificationSucceeded();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Candidate schema verification failed with {ErrorCount} errors: {Errors}"
    )]
    public partial void SchemaVerificationFailed(int errorCount, string errors);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Schema registry failed during candidate verification. ErrorCode: {ErrorCode}, Message: {ErrorMessage}"
    )]
    public partial void SchemaVerificationRegistryError(string errorCode, string errorMessage);
}
