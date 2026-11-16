using AwesomeAssertions;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Fixtures;
using Xunit;

namespace EstateDataCollection.Tests.Integration;

[Collection("AzuriteCollection")]
public class BlobSubmissionIntegrationTests(AzuriteFixture fixture)
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);

    [Fact]
    public async Task When_Executing_Complete_Ingestion_Lifecycle_Against_Azurite_It_Should_Persist_Draft_And_Promote_To_Processed()
    {
        var session = new SubmissionSessionBuilder()
            .WithSchema("edc_payload", 1)
            .Build();

        (await _harness.SubmissionInfrastructure.CreateSessionAsync(session)).ShouldSucceed();

        (await _harness.SubmissionInfrastructure.SaveDraftPayloadAsync(
            session.SubmissionId,
            104521,
            SurveyPayloadFixtures.ValidPayloadJson)).ShouldSucceed();

        (await _harness.SubmissionInfrastructure.PromoteToProcessedAsync(
            session.SubmissionId,
            104521,
            "edc_payload",
            1)).ShouldSucceed();

        var draftExists = await _harness.SubmissionInfrastructure.GetDraftPayloadAsync(session.SubmissionId);
        draftExists.IsFailure.Should().BeTrue();
    }
}
