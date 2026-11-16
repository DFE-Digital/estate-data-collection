using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Azure.Storage.Blobs;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Fixtures;

/// <summary>
/// Provides a live Azurite Blob Storage client for integration tests.
/// Uses an already-running Azurite instance on 127.0.0.1:10000 if available (e.g. GitHub Actions service container),
/// and otherwise launches an ephemeral Azurite Docker container automatically on a free host port.
/// </summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    private const string AccountName = "devstoreaccount1";
    private const string AccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    public const string DefaultConnectionString =
        $"DefaultEndpointsProtocol=http;AccountName={AccountName};AccountKey={AccountKey};BlobEndpoint=http://127.0.0.1:10000/{AccountName};";

    private string? _ownedContainerId;
    private string? _dockerExecutable;

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
    }

    public async Task DisposeAsync()
    {
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

    private async Task EnsureContainersAsync()
    {
        var containers = new[] { "schemas", "draft-schemas", "payloads", "processed" };
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                foreach (var name in containers)
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
