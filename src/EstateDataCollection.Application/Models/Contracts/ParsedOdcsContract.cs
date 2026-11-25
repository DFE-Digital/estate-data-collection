using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Models.Contracts;

public sealed record ParsedOdcsContract(
    string? ContractId,
    string? Name,
    string? Version,
    string GeneratedSchemaJson,
    string? GeneratedRulesJson,
    IReadOnlyList<BusinessRule> ExtractedRules,
    SchemaValidationPolicy? ValidationPolicy = null
);
