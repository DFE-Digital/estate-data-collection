namespace EstateDataCollection.Common.Messages;

public static class ErrorMessages
{
    public const string MessagePlaceholder = "{Message}";

    public const string SchemaVerificationFailed = "Candidate schema failed pre-flight tests.";
    public const string SchemaRetired = "Schema '{SchemaName}' v{Version} is retired.";
    public const string SchemaContractViolation = "Payload failed schema validation.";
    public const string SchemaNotFound = "Schema '{SchemaName}' v{Version} does not exist.";
    public const string SchemaAlreadyExists = "Schema '{SchemaName}' version {Version} already exists and is immutable.";
    public const string SchemaPersistFailed = "Could not save schema to blob container: {Message}";
    public const string SchemaNotActive = "Schema '{SchemaName}' v{Version} is not active or has not been scheduled yet.";
    public const string SchemaInvalid = "Schema '{SchemaName}' v{Version} is Invalid and cannot be scheduled or activated. Push a fixed contract to a new version.";
    public const string SchemaUnvalidated = "Schema '{SchemaName}' v{Version} is Unvalidated and requires explicit sign-off before scheduling or activating.";
    public const string SchemaDeleted = "Schema '{SchemaName}' v{Version} has been deleted and its version key is reserved. Push fixes to a new version.";
    public const string SchemaSignOffInvalidState = "Schema '{SchemaName}' v{Version} cannot be signed off from state '{State}'. Only Unvalidated schemas can be signed off.";

    public const string PayloadEmpty = "Payload cannot be empty.";
    public const string PayloadNotFound = "Payload not found.";
    public const string SubmissionAlreadyFinalised = "This submission has already been finalised and cannot be modified.";
    public const string SubmissionNotFound = "Submission \"{SubmissionId}\" not found.";

    public const string PayloadTooLargeTitle = "Payload Too Large";
    public static string RequestBodyExceedsLimitMb(int maxSizeMb) =>
        $"The request payload exceeds the configured maximum limit of {maxSizeMb} MB.";
    public static string RequestBodyTooLarge(long maxSizeBytes) =>
        $"The request payload exceeds the configured maximum limit of {Math.Max(1, maxSizeBytes / (1024 * 1024))} MB.";

    public const string InvalidScheduleRequestBody = "Invalid schedule request JSON body.";
    public const string InvalidSignOffRequestBody = "Invalid sign-off request JSON body.";
    public const string InvalidRunSchemaTestsRequestBody = "Invalid run schema tests request JSON body.";
    public const string InvalidSubmitSchemaRequestBody = "Invalid submit schema request JSON body.";
    public const string InvalidUpdateSchemaTestsRequestBody = "Invalid update schema tests request JSON body.";
}
