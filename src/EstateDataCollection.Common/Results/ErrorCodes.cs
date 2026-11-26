using System.ComponentModel.DataAnnotations;
using EstateDataCollection.Common.Messages;

namespace EstateDataCollection.Common.Results;

public enum ErrorCodes
{
    None = 0,
    
    #region Schema Validation

    [Display(Name = "Schema.VerificationFailed", Description = ErrorMessages.SchemaVerificationFailed)]
    SchemaVerificationFailed = 1000,

    [Display(Name = "Schema.Retired", Description = ErrorMessages.SchemaRetired)]
    SchemaRetired = 1001,

    [Display(Name = "Schema.ContractViolation", Description = ErrorMessages.SchemaContractViolation)]
    SchemaContractViolation = 1002,
    
    [Display(Name = "Schema.NotFound", Description = ErrorMessages.SchemaNotFound)]
    SchemaNotFound = 1003,
    
    [Display(Name = "Schema.AlreadyExists", Description = ErrorMessages.SchemaAlreadyExists)]
    SchemaAlreadyExists = 1004,
    
    [Display(Name = "Schema.InvalidSchedule", Description = ErrorMessages.MessagePlaceholder)]
    SchemaInvalidSchedule = 1005,
    
    [Display(Name = "Schema.PersistFailed", Description = ErrorMessages.SchemaPersistFailed)]
    SchemaPersistFailed = 1006,
    
    [Display(Name = "Schema.NotActive", Description = ErrorMessages.SchemaNotActive)]
    SchemaNotActive = 1007,
    
    [Display(Name = "Schema.InvalidIdentifierConfig", Description = ErrorMessages.MessagePlaceholder)]
    SchemaInvalidIdentifierConfig = 1008,
    
    [Display(Name = "Schema.Invalid", Description = ErrorMessages.SchemaInvalid)]
    SchemaInvalid = 1009,
    
    [Display(Name = "Schema.Unvalidated", Description = ErrorMessages.SchemaUnvalidated)]
    SchemaUnvalidated = 1010,
    
    [Display(Name = "Schema.Deleted", Description = ErrorMessages.SchemaDeleted)]
    SchemaDeleted = 1011,
    
    [Display(Name = "Schema.SignOffInvalidState", Description = ErrorMessages.SchemaSignOffInvalidState)]
    SchemaSignOffInvalidState = 1012,

    #endregion
    
    #region Payload Validation
    
    [Display(Name = "Payload.Empty", Description = ErrorMessages.PayloadEmpty)]
    PayloadEmpty = 2000,
    
    [Display(Name = "Payload.NotFound", Description = ErrorMessages.PayloadNotFound)]
    PayloadNotFound = 2002,
    
    [Display(Name = "Payload.InvalidIdentifier", Description = ErrorMessages.MessagePlaceholder)]
    PayloadInvalidIdentifier = 2003,
    
    #endregion
    
    #region Json Validation
    
    [Display(Name = "Json.Malformed", Description = ErrorMessages.MessagePlaceholder)]
    JsonMalformed = 3000,
    
    [Display(Name = "Json.Empty", Description = ErrorMessages.PayloadEmpty)]
    JsonEmptyPayload = 3001,
    
    #endregion
    
    #region Storage
    
    [Display(Name = "Storage.UploadFailed", Description = ErrorMessages.MessagePlaceholder)]
    StorageUploadFailed = 4000,
    
    [Display(Name = "Storage.ReadFailed", Description = ErrorMessages.MessagePlaceholder)]
    StorageReadFailed = 4001,
    
    [Display(Name = "Storage.SerialisationError", Description = ErrorMessages.MessagePlaceholder)]
    StorageSerialisationError = 4002,
    
    [Display(Name = "Storage.PromotionFailed", Description = ErrorMessages.MessagePlaceholder)]
    StoragePromotionFailed = 4003,
    
    #endregion
    
    #region Submission

    [Display(Name = "Submission.AlreadyFinalised", Description = ErrorMessages.SubmissionAlreadyFinalised)]
    SubmissionAlreadyFinalised = 5000,

    [Display(Name = "Submission.NotFound", Description = ErrorMessages.SubmissionNotFound)]
    SubmissionNotFound = 5001,

    #endregion
}
