using System.Text.Json.Serialization;
using EstateDataCollection.Common.Converters;

namespace EstateDataCollection.Common.Results;

/// <summary>
/// Severity tier for quality rules and validation findings.
/// Lower numeric values represent higher severity so threshold gating can use
/// <c>severity &lt;= FailOnMinimumSeverity</c>.
/// </summary>
[JsonConverter(typeof(RuleSeverityJsonConverter))]
public enum RuleSeverity
{
    Critical = 0,
    Error = 1,
    Warning = 2,
    Info = 3
}