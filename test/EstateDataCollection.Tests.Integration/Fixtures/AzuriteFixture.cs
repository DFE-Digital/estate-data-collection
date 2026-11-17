using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Fixtures;

/// <summary>
/// Provides an isolated, live Azurite Blob Storage client for integration tests.
/// Connects to an already-running Azurite instance via <c>AZURE_STORAGE_CONNECTION_STRING</c>,
/// <c>ConnectionStrings__BlobStorage</c>, or <c>UseDevelopmentStorage=true</c> (<c>127.0.0.1:10000</c>).
/// Every run operates inside unique <c>test-{runId}-*</c> containers and deletes them on <see cref="DisposeAsync"/>
/// so local development containers (<c>schemas</c>, <c>draft-schemas</c>, <c>payloads</c>, <c>processed</c>) stay clean.
/// </summary>
public sealed partial class AzuriteFixture : IAsyncLifetime
{
    public const string DefaultConnectionString = "UseDevelopmentStorage=true";

    private readonly string _runId = Guid.NewGuid().ToString("N")[..12];

    public string SchemasContainerName => $"test-{_runId}-schemas";
    public string DraftSchemasContainerName => $"test-{_runId}-draft-schemas";
    public string PayloadsContainerName => $"test-{_runId}-payloads";
    public string ProcessedContainerName => $"test-{_runId}-processed";

    public IReadOnlyList<string> ContainerNames =>
    [
        SchemasContainerName,
        DraftSchemasContainerName,
        PayloadsContainerName,
        ProcessedContainerName
    ];

    public string ConnectionString { get; private set; } = DefaultConnectionString;
    public BlobServiceClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        ConnectionString = ResolveConnectionString();
        Client = new BlobServiceClient(ConnectionString);
        await EnsureContainersAsync();
        await CleanupStaleTestContainersAsync(TimeSpan.FromHours(1));
    }

    public async Task DisposeAsync()
    {
        if (Client is not null)
        {
            foreach (var name in ContainerNames)
            {
                try
                {
                    await Client.GetBlobContainerClient(name).DeleteIfExistsAsync();
                }
                catch
                {
                    // Best-effort cleanup of test-run containers
                }
            }
        }
    }

    public static bool IsTestContainerName(string name)
    {
        return TestContainerPattern().IsMatch(name);
    }

    [GeneratedRegex(@"^test-[0-9a-f]{12}-(schemas|draft-schemas|payloads|processed)$", RegexOptions.CultureInvariant)]
    private static partial Regex TestContainerPattern();

    private static string ResolveConnectionString()
    {
        var envConnection =
            Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ??
            Environment.GetEnvironmentVariable("ConnectionStrings__BlobStorage");

        return string.IsNullOrWhiteSpace(envConnection)
            ? DefaultConnectionString
            : envConnection.Trim();
    }

    private async Task EnsureContainersAsync()
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                foreach (var name in ContainerNames)
                {
                    await Client.GetBlobContainerClient(name).CreateIfNotExistsAsync();
                }

                return;
            }
            catch (Exception ex) when (attempt < 9)
            {
                lastError = ex;
                await Task.Delay(300);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            "Could not connect to Azurite Blob Storage for integration tests. " +
            "Ensure Azurite is running on 127.0.0.1:10000 (or set AZURE_STORAGE_CONNECTION_STRING / ConnectionStrings__BlobStorage).",
            lastError);
    }

    private async Task CleanupStaleTestContainersAsync(TimeSpan maxAge)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow - maxAge;
            await foreach (var container in Client.GetBlobContainersAsync(prefix: "test-"))
            {
                if (!IsTestContainerName(container.Name) ||
                    ContainerNames.Contains(container.Name, StringComparer.Ordinal))
                {
                    continue;
                }

                if (container.Properties.LastModified <= cutoff)
                {
                    await Client.GetBlobContainerClient(container.Name).DeleteIfExistsAsync();
                }
            }
        }
        catch
        {
            // Best-effort sweep of orphaned containers from aborted runs
        }
    }
}
