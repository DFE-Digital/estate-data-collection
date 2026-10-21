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
    
    [Display(Name = "Schema.AlreadyExists", Description =  "Schema '{SchemaName}' version {Version} already exists and is immutable.")]
    SchemaAlreadyExists = 1004,
    
    [Display(Name = "Schema.InvalidSchedule", Description = "{Message}")]
    SchemaInvalidSchedule = 1005,
    
    [Display(Name = "Schema.PersistFailed", Description = "Could not save schema to blob container: {Message}")]
    SchemaPersistFailed = 1006,

    #endregion

    #region Payload Validation
    
    [Display(Name = "Payload.Empty", Description = "Payload cannot be empty.")]
    PayloadEmpty = 2000,

    [Display(Name = "Payload.InvalidUrn", Description = "The 'urn' property must be a positive integer.")]
    PayloadInvalidUrn = 2001,

    [Display(Name = "Payload.NotFound", Description = "Payload not found.")]
    PayloadNotFound = 2002,

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
