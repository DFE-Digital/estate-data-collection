using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Application.Validators;

public sealed partial class SchemaVerificationServiceInstrumentation(
    ILogger<SchemaVerificationService> logger
) : IInstrumentation
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Candidate schema verification completed. Valid: {IsValid}, SyntaxErrors: {SyntaxErrorCount}, FixtureErrors: {FixtureErrorCount}"
    )]
    public partial void CandidateVerificationCompleted(bool isValid, int syntaxErrorCount, int fixtureErrorCount);
}
