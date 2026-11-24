using System.Text.Json.Serialization;

namespace EstateDataCollection.Domain.Submission;

[JsonConverter(typeof(JsonStringEnumConverter<SubmissionProcessOutcome>))]
public enum SubmissionProcessOutcome
{
    Succeeded = 0,
    Failed = 1,
    ValidationErrorsFound = 2,
    Skipped = 3,
    Missing = 4
}