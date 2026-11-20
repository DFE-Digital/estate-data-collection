using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
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
        var submissions = _harness.Get<ISubmissionInfrastructure>();
        var session = new SubmissionSessionBuilder()
            .WithSchema("edc_payload", 1)
            .Build();

        (await submissions.CreateSessionAsync(session)).ShouldSucceed();

        (await submissions.SaveDraftPayloadAsync(
            session.SubmissionId,
            104521,
            SurveyPayloadFixtures.ValidPayloadJson)).ShouldSucceed();

        (await submissions.PromoteToProcessedAsync(
            session.SubmissionId,
            104521,
            "edc_payload",
            1)).ShouldSucceed();

        var draftExists = await submissions.GetDraftPayloadAsync(session.SubmissionId);
        draftExists.IsFailure.Should().BeTrue();
    }
}
