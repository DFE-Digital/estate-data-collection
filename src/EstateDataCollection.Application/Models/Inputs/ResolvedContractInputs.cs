namespace EstateDataCollection.Application.Models.Inputs;

public sealed record ResolvedContractInputs(
    string? SchemaJson,
    string? RulesJson,
    string? RawOdcsYaml,
    string? ContractId = null,
    string? Version = null
);
