using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Tests.Common.Extensions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Domain;

public sealed class DraftPayloadMergerTests
{
    [Fact]
    public void Merge_Should_DeepMerge_Nested_Objects_And_Replace_Arrays_Chronologically()
    {
        var t1 = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(10);
        var t3 = t1.AddMinutes(20);

        // Intentionally supply out-of-order to verify sorting by CreatedAtUtc
        IReadOnlyList<SubmissionDraftItem> items =
        [
            new("sub/drafts/02.json", t2, """{"school":{"name":"Oak Academy"},"blocks":["B1","B2"],"temp":"remove-me"}""", 70),
            new("sub/drafts/03.json", t3, """{"temp":null,"survey":{"completed":true}}""", 42),
            new("sub/drafts/01.json", t1, """{"urn":104521,"school":{"urn":104521},"blocks":["OLD"]}""", 56)
        ];

        var mergedJson = DraftPayloadMerger.Merge(items).ShouldSucceed();

        using var doc = JsonDocument.Parse(mergedJson);
        var root = doc.RootElement;

        root.GetProperty("urn").GetInt32().Should().Be(104521);
        root.GetProperty("school").GetProperty("urn").GetInt32().Should().Be(104521);
        root.GetProperty("school").GetProperty("name").GetString().Should().Be("Oak Academy");
        root.GetProperty("blocks").EnumerateArray().Select(e => e.GetString()).Should().Equal("B1", "B2");
        root.TryGetProperty("temp", out _).Should().BeFalse();
        root.GetProperty("survey").GetProperty("completed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void MergeJsonPayloads_With_Malformed_Json_Should_Return_Validation_Error()
    {
        var result = DraftPayloadMerger.MergeJsonPayloads(
        [
            """{"urn":104521}""",
            "{not-valid-json"
        ]);

        result.ShouldFailWith("Json.Malformed");
    }

    [Fact]
    public void Merge_With_Empty_List_Should_Return_PayloadEmpty()
    {
        var result = DraftPayloadMerger.Merge([]);
        result.ShouldFailWith("Payload.Empty");
    }
}
