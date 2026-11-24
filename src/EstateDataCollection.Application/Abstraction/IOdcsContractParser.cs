using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Abstraction;

public interface IOdcsContractParser : IInfrastructure
{
    /// <summary>
    /// Determines whether the provided file name, content type, or raw content represents a YAML/ODCS contract.
    /// </summary>
    bool IsOdcsYaml(string? content, string? fileName = null, string? contentType = null);

    /// <summary>
    /// Parses an ODCS v3.2.0 YAML contract into a generated JSON Schema and extracted quality rules.
    /// </summary>
    Result<ParsedOdcsContract> Parse(string odcsYaml, string? targetSchemaName = null);
}
