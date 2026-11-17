using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Fixtures;

/// <summary>
/// Provides an isolated, live Azurite Blob Storage client for integration tests.
/// Uses an already-running Azurite instance on 127.0.0.1:10000 if available (e.g. local dev or CI service container),
/// and otherwise launches an ephemeral Azurite Docker container automatically on a free host port.
/// Every run operates inside unique <c>test-{runId}-*</c> containers and deletes them on <see cref="DisposeAsync"/>
/// so local development containers (<c>schemas</c>, <c>draft-schemas</c>, <c>payloads</c>, <c>processed</c>) stay clean.
/// </summary>
public sealed partial class AzuriteFixture : IAsyncLifetime
{
    private const string AccountName = "devstoreaccount1";
    private const string AccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    public const string DefaultConnectionString =
        $"DefaultEndpointsProtocol=http;AccountName={AccountName};AccountKey={AccountKey};BlobEndpoint=http://127.0.0.1:10000/{AccountName};";

    private readonly string _runId = Guid.NewGuid().ToString("N")[..12];
    private string? _ownedContainerId;
    private string? _dockerExecutable;

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
        if (await IsPortOpenAsync("127.0.0.1", 10000))
        {
            ConnectionString = DefaultConnectionString;
        }
        else
        {
            var hostPort = GetFreeTcpPort();
            await StartEphemeralDockerAzuriteAsync(hostPort);
            ConnectionString =
                $"DefaultEndpointsProtocol=http;AccountName={AccountName};AccountKey={AccountKey};BlobEndpoint=http://127.0.0.1:{hostPort}/{AccountName};";
            await WaitForPortOpenAsync("127.0.0.1", hostPort, TimeSpan.FromSeconds(20));
        }

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

        if (!string.IsNullOrWhiteSpace(_ownedContainerId) && !string.IsNullOrWhiteSpace(_dockerExecutable))
        {
            try
            {
                await RunProcessAsync(_dockerExecutable, $"rm -f {_ownedContainerId}");
            }
            catch
            {
                // Best-effort cleanup of ephemeral container
            }
        }
    }

    public static bool IsTestContainerName(string name)
    {
        return TestContainerPattern().IsMatch(name);
    }

    [GeneratedRegex(@"^test-[0-9a-f]{12}-(schemas|draft-schemas|payloads|processed)$", RegexOptions.CultureInvariant)]
    private static partial Regex TestContainerPattern();

    private async Task EnsureContainersAsync()
    {
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
            catch when (attempt < 9)
            {
                await Task.Delay(300);
            }
        }
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

    private async Task StartEphemeralDockerAzuriteAsync(int hostPort)
    {
        _dockerExecutable = ResolveDockerExecutable();
        if (string.IsNullOrWhiteSpace(_dockerExecutable))
        {
            throw new InvalidOperationException(
                "Azurite is not listening on 127.0.0.1:10000 and Docker CLI could not be found to auto-start an Azurite container.");
        }

        var containerName = $"edc-azurite-{Guid.NewGuid():N}";
        var args =
            $"run -d --rm --name {containerName} -p {hostPort}:10000 mcr.microsoft.com/azure-storage/azurite:latest " +
            "azurite-blob --blobHost 0.0.0.0 --blobPort 10000";

        var (exitCode, stdout, stderr) = await RunProcessAsync(_dockerExecutable, args);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            throw new InvalidOperationException(
                $"Failed to start Azurite Docker container (exit {exitCode}): {stderr}");
        }

        _ownedContainerId = stdout.Trim();
    }

    private static string? ResolveDockerExecutable()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(home, ".docker", "bin", "docker"),
            "/usr/local/bin/docker",
            "/opt/homebrew/bin/docker",
            "/usr/bin/docker"
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim(), OperatingSystem.IsWindows() ? "docker.exe" : "docker");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await client.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task WaitForPortOpenAsync(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsPortOpenAsync(host, port))
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Timed out waiting for Azurite on {host}:{port}.");
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
