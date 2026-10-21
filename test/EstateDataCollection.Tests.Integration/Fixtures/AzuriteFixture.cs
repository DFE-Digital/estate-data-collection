using Azure.Storage.Blobs;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Fixtures;

public class AzuriteFixture : IAsyncLifetime
{
    public const string ConnectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";
    public BlobServiceClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Client = new BlobServiceClient(ConnectionString);
        await Client.GetBlobContainerClient("schemas").CreateIfNotExistsAsync();
        await Client.GetBlobContainerClient("payloads").CreateIfNotExistsAsync();
        await Client.GetBlobContainerClient("processed").CreateIfNotExistsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
