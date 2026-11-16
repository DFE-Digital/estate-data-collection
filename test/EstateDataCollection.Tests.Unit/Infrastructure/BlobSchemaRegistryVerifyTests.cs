using Azure.Storage.Blobs;
using AwesomeAssertions;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public class BlobSchemaRegistryVerifyTests
{
    [Fact]
    public async Task VerifyCandidateSchema_Should_Validate_InFlight_Fixtures_For_Both_OdcsYaml_And_Split_Json()
    {
        var blobServiceClient = Substitute.For<BlobServiceClient>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new BlobSchemaRegistry(blobServiceClient, Options.Create(new BlobSchemaRegistryOptions()), cache);

        var odcsYaml = CdeContractFixtureLoader.LoadFile("contracts/edc-school-site-jsonlogic.odcs.yaml");
        var schemaJson = CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.schema.json");
        var rulesJson = CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.rules.json");

        var yamlOutcome = (await registry.VerifyCanditateSchemaAsync(
            rawSchemaJson: odcsYaml,
            rawRulesJson: null,
            sampleValidPayloads: [CdePayloadFixtures.ValidSchoolSitePayload],
            sampleInvalidPayloads: [CdePayloadFixtures.InvalidTimestampSchoolSitePayload])).ShouldSucceed();

        var jsonOutcome = (await registry.VerifyCanditateSchemaAsync(
            rawSchemaJson: schemaJson,
            rawRulesJson: rulesJson,
            sampleValidPayloads: [CdePayloadFixtures.ValidSchoolSitePayload],
            sampleInvalidPayloads: [CdePayloadFixtures.InvalidTimestampSchoolSitePayload])).ShouldSucceed();

        var failedInFlight = (await registry.VerifyCanditateSchemaAsync(
            rawSchemaJson: schemaJson,
            rawRulesJson: rulesJson,
            sampleValidPayloads: [CdePayloadFixtures.InvalidTimestampSchoolSitePayload],
            sampleInvalidPayloads: [])).ShouldSucceed();

        yamlOutcome.IsSuccessful.Should().BeTrue();
        yamlOutcome.Errors.Should().BeEmpty();

        jsonOutcome.IsSuccessful.Should().BeTrue();
        jsonOutcome.Errors.Should().BeEmpty();

        failedInFlight.IsSuccessful.Should().BeFalse();
        failedInFlight.FixtureErrors.Should().ContainSingle(e =>
            e.Identifier == "SampleValidFixtures:0" &&
            e.Message.Contains("SampleValidFixtures:0 violated quality rules") &&
            e.Message.Contains("/survey"));
    }
}
