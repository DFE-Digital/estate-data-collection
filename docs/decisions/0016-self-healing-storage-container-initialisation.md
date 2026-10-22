---
status: "proposed"
sub-status: "todo"
date: 2026-10-22
decision-makers: ["Chris Kelly"]
---

# 16. Self-Healing Storage Container Initialisation on Application Startup

## Context and Problem Statement

The Estate Data Collection (EDC) service relies on dedicated Azure Blob Storage containers (`schemas`, `payloads`, `processed`, `landing`, `cleared`, `quarantine`) as detailed in [ADR 0003](0003-multi-container-blob-storage-landing-zone.md). 

Currently, the application codebase does not ensure that these containers exist before attempting read or write operations. While integration test fixtures ([`AzuriteFixture.cs`](../../test/EstateDataCollection.Tests.Integration/Fixtures/AzuriteFixture.cs)) create containers for testing, running the application locally (`func start`), deploying to a brand-new ephemeral cloud environment, or executing tests without pre-existing storage resources leads to runtime exceptions:
```
Azure.RequestFailedException: The specified container does not exist. (Status 404: ContainerNotFound)
```

Furthermore, in locked-down production environments governed by Infrastructure as Code (Terraform / Bicep), the application's Microsoft Entra ID Managed Identity may possess data-plane permissions (`Storage Blob Data Contributor`) but lack control-plane container provisioning permissions (`Microsoft.Storage/storageAccounts/blobServices/containers/write`), meaning hardcoded container creation on every request could fail with authorization errors.

How should the application ensure storage containers are available on boot while respecting infrastructure security policies and zero-trust permission models?

## Decision Drivers

* **Zero-Friction Local & CI Ergonomics:** Developers running `func start` and CI/CD pipelines executing integration tests against fresh Azurite instances must not require manual container creation scripts.
* **Fail-Safe Startup Verification:** Detect missing storage endpoints or configuration issues immediately upon application boot rather than failing mid-flight during surveyor ingestion.
* **Least-Privilege Cloud Compatibility:** Must not crash or fail startup in locked-down production environments where containers are pre-provisioned by Terraform and the application identity lacks control-plane container creation rights.
* **Performance:** Avoid calling `CreateIfNotExistsAsync` on high-throughput hot request paths (`PUT /submissions/{id}`).

## Considered Options

* **Option 1: Self-Healing Startup `IHostedService` Initialiser with Graceful Permission Fallback (Proposed)**
* **Option 2: Lazy Container Creation Inside Repositories on First Request**
* **Option 3: Purely External Infrastructure Provisioning (Terraform / Azurite Shell Scripts)**

## Decision Outcome

Chosen option: **"Option 1: Self-Healing Startup `IHostedService` Initialiser with Graceful Permission Fallback"**, because it isolates container readiness to application boot, prevents per-request storage latency, and safely handles restricted cloud identities:

1. **`BlobStorageInitialiser` (`IHostedService`)**:
   Registered in `EstateDataCollection.Infrastructure`:
   ```csharp
   public sealed class BlobStorageInitialiser(
       BlobServiceClient blobServiceClient,
       IOptions<BlobSchemaRegistryOptions> schemaOptions,
       IOptions<BlobSubmissionRepositoryOptions> submissionOptions,
       ILogger<BlobStorageInitialiser> logger) : IHostedService
   {
       public async Task StartAsync(CancellationToken cancellationToken)
       {
           var containers = new[]
           {
               schemaOptions.Value.ContainerName,
               submissionOptions.Value.PayloadContainerName,
               submissionOptions.Value.ProcessedContainerName
           };

           foreach (var container in containers.Distinct())
           {
               try
               {
                   var client = blobServiceClient.GetBlobContainerClient(container);
                   await client.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                   logger.LogInformation("Verified storage container '{Container}'.", container);
               }
               catch (RequestFailedException ex) when (ex.Status == 403)
               {
                   // In locked-down production, container creation is restricted to Terraform
                   logger.LogWarning("Insufficient permissions to create container '{Container}'. Assuming pre-provisioned by IaC.", container);
               }
           }
       }

       public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
   }
   ```
2. **Execution Timing**:
   Runs during host startup before the HTTP listener begins accepting incoming surveyor traffic.
3. **Graceful Cloud Handling**:
   - In **Local Development / CI (Azurite)**: Automatically creates `schemas`, `payloads`, and `processed` containers.
   - In **Production (Azure)**: If containers already exist or if Entra ID Managed Identity lacks control-plane permissions (`403 Forbidden`), the initialiser catches the exception, logs a warning, and allows the application to proceed normally.

### Consequences

* Good, because new developers and CI test runners can run against a clean Azurite instance without running manual container setup scripts.
* Good, because zero network overhead is added to the hot request path; repositories execute point blob operations directly.
* Good, because production environments with strict RBAC segregation are not broken by runtime container creation attempts.
* Bad, because it adds an asynchronous startup task during application initialization.

### Confirmation

Compliance will be confirmed via:
- Automated integration tests running against a blank Azurite instance with all containers created automatically on boot.
- Unit tests verifying `BlobStorageInitialiser` handles `403 RequestFailedException` without crashing host startup.
- Verification that no `CreateIfNotExistsAsync` calls exist within `BlobSchemaRegistry` or `BlobSubmissionInfrastructure` hot paths.

## Pros and Cons of the Options

### Option 1: Startup IHostedService Initialiser (Proposed)

* Good, because it executes once at startup rather than on every HTTP request.
* Good, because it provides clear startup log diagnostics for missing or misnamed containers.
* Good, because it cleanly separates storage readiness from request handling.
* Bad, because container names must be aggregated from registered options models.

### Option 2: Lazy Creation in Repositories

* Good, because containers are created on-demand only when first accessed.
* Bad, because every write operation incurs a check or requires synchronization locks (`Lazy<Task>`).
* Bad, because missing storage credentials fail late during user requests rather than failing fast on boot.

### Option 3: Purely External Infrastructure Scripts

* Good, because application code has zero infrastructure provisioning logic.
* Bad, because developers must manually execute setup commands every time Azurite is restarted or cleared.
* Bad, because CI test pipelines require extra orchestration steps before executing `dotnet test`.

## More Information

Related ADRs:
* [ADR 0003: Multi-Container Blob Storage Landing Zone](0003-multi-container-blob-storage-landing-zone.md)
* [ADR 0012: Local Developer Orchestration with Azurite](0012-local-developer-orchestration-and-http-specifications.md)
