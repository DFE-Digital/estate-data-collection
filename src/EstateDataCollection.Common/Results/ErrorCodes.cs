using System.ComponentModel.DataAnnotations;

namespace EstateDataCollection.Common.Results;

public enum ErrorCodes
{
    None = 0,
    
    #region Schema Validation

    [Display(Name = "Schema.VerificationFailed", Description = "Candidate schema failed pre-flight tests.")]
    SchemaVerificationFailed = 1000,

    [Display(Name = "Schema.Retired", Description = "Schema '{SchemaName}' v{Version} is retired.")]
    SchemaRetired = 1001,

    [Display(Name = "Schema.ContractViolation", Description = "Payload failed schema validation.")]
    SchemaContractViolation = 1002,
    
    [Display(Name = "Schema.NotFound", Description = "Schema '{SchemaName}' v{Version} does not exist.")]
    SchemaNotFound = 1003,
    
    [Display(Name = "Schema.AlreadyExists", Description = "Schema '{SchemaName}' version {Version} already exists and is immutable.")]
    SchemaAlreadyExists = 1004,
    
    [Display(Name = "Schema.InvalidSchedule", Description = "{Message}")]
    SchemaInvalidSchedule = 1005,
    
    [Display(Name = "Schema.PersistFailed", Description = "Could not save schema to blob container: {Message}")]
    SchemaPersistFailed = 1006,
    
    [Display(Name = "Schema.NotActive", Description = "Schema '{SchemaName}' v{Version} is not active or has not been scheduled yet.")]
    SchemaNotActive = 1007,

    [Display(Name = "Schema.InvalidIdentifierConfig", Description = "{Message}")]
    SchemaInvalidIdentifierConfig = 1008,

    [Display(Name = "Schema.Invalid", Description = "Schema '{SchemaName}' v{Version} is Invalid and cannot be scheduled or activated. Push a fixed contract to a new version.")]
    SchemaInvalid = 1009,

    [Display(Name = "Schema.Unvalidated", Description = "Schema '{SchemaName}' v{Version} is Unvalidated and requires explicit sign-off before scheduling or activating.")]
    SchemaUnvalidated = 1010,

    [Display(Name = "Schema.Deleted", Description = "Schema '{SchemaName}' v{Version} has been deleted and its version key is reserved. Push fixes to a new version.")]
    SchemaDeleted = 1011,

    [Display(Name = "Schema.SignOffInvalidState", Description = "Schema '{SchemaName}' v{Version} cannot be signed off from state '{State}'. Only Unvalidated schemas can be signed off.")]
    SchemaSignOffInvalidState = 1012,

    #endregion
    
    #region Payload Validation
    
    [Display(Name = "Payload.Empty", Description = "Payload cannot be empty.")]
    PayloadEmpty = 2000,

    [Display(Name = "Payload.NotFound", Description = "Payload not found.")]
    PayloadNotFound = 2002,

    [Display(Name = "Payload.InvalidIdentifier", Description = "{Message}")]
    PayloadInvalidIdentifier = 2003,
    
    #endregion
    
    #region Json Validation
    
    [Display(Name = "Json.Malformed", Description = "{Message}")]
    JsonMalformed = 3000,
    
    [Display(Name = "Json.Empty", Description = "Payload cannot be empty.")]
    JsonEmptyPayload = 3001,
    
    #endregion
    
    #region Storage
    
    [Display(Name = "Storage.UploadFailed", Description = "{Message}")]
    StorageUploadFailed = 4000,
    
    [Display(Name = "Storage.ReadFailed", Description = "{Message}")]
    StorageReadFailed = 4001,
    
    [Display(Name = "Storage.SerialisationError", Description = "{Message}")]
    StorageSerialisationError = 4002,
    
    [Display(Name = "Storage.PromotionFailed", Description = "{Message}")]
    StoragePromotionFailed = 4003,
    
    #endregion
    
    #region Submission

    [Display(Name = "Submission.AlreadyFinalised", Description = "This submission has already been finalised and cannot be modified.")]
    SubmissionAlreadyFinalised = 5000,

    [Display(Name = "Submission.NotFound", Description = "Submission \"{SubmissionId}\" not found.")]
    SubmissionNotFound = 5001,

    #endregion
}
