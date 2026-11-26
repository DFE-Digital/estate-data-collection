using System.Text.Json.Nodes;

namespace EstateDataCollection.Tests.Common.Builders.Payloads;

public sealed record CcdElementalV3InvalidCaseDefinition(
    string Description,
    IReadOnlyList<string> ExpectedErrors,
    IReadOnlyDictionary<string, JsonNode?> MutatedFields);