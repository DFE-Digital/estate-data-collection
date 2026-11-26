using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Models.Inputs;

public sealed record ResolvedContractInputs(
    string? SchemaJson,
    string? RulesJson,
    string? RawOdcsYaml,
    string? ContractId = null,
    string? Version = null,
    SchemaValidationPolicy? ValidationPolicy = null
);
