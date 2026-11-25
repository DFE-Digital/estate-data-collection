using System.Text.Json.Serialization;

namespace EstateDataCollection.Domain.Schema;

[JsonConverter(typeof(JsonStringEnumConverter<CrossSubmissionDraftPolicy>))]
public enum CrossSubmissionDraftPolicy
{
    AllowDraftReferences = 0,
    WarnOnDraftReferences = 1,
    BlockFinaliseOnDraftReferences = 2
}
