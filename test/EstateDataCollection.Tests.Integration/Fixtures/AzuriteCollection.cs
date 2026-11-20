using Xunit;

namespace EstateDataCollection.Tests.Integration.Fixtures;

[CollectionDefinition("AzuriteCollection")]
public sealed class AzuriteCollection : ICollectionFixture<AzuriteFixture>;
