---
status: "accepted"
sub-status: "implemented"
date: 2026-11-17
decision-makers: ["Chris Kelly"]
---

# 21. Isolated Ephemeral Azurite Containers and Automatic Test Storage Cleanup

## Context and Problem Statement

Integration tests across `EstateDataCollection.Tests.Integration` exercise live Azure Blob Storage operations via Azurite (`AzuriteFixture`). Previously, `AzuriteFixture` created and wrote into the exact same container names used by local development (`schemas`, `draft-schemas`, `payloads`, `processed`) and never deleted test data on teardown.

As integration test coverage grew (schema submissions, regression fixture updates, draft ingestion, and Admin UI flows), repeated `dotnet test` runs polluted the shared local Azurite instance with stale test schemas, submissions, and fixture blobs — causing false positives/conflicts for developers running `func start` locally against Azurite. Similarly, partial writes while updating stored schema fixtures risked leaving orphaned blobs if a secondary fixture write failed. How should integration tests and schema test updates isolate and clean up blob storage state?

## Decision Drivers

* **Zero Pollution of Local Development Containers:** Running `dotnet test` against a shared local Azurite instance must never leave test artefacts inside `schemas`, `draft-schemas`, `payloads`, or `processed`.
* **CI & Dev Container Compatibility:** Connect directly to an already-running Azurite endpoint (`127.0.0.1:10000`, CI service container, or Dev Container service via `AZURE_STORAGE_CONNECTION_STRING` / `ConnectionStrings__BlobStorage`) without spawning child `docker` processes from test code.
* **Parallel / Repeat Run Safety:** Test runs must not collide with previous runs or crashed test runs.
* **Best-Effort Orphan Recovery:** If a test run is aborted (`SIGINT`/IDE stop) before `DisposeAsync`, stale ephemeral test containers must be cleaned up automatically on subsequent runs.
* **Fixture Write Atomicity:** Updating valid/invalid test fixtures in `BlobSchemaRegistry` must roll back or delete cleared fixture blobs cleanly so stale JSON fixtures do not linger.

## Considered Options

* **Option 1: Per-Run Unique Ephemeral Container Names (`test-{runId}-*`) with `DisposeAsync` Deletion and Stale Container Sweeps**
* **Option 2: Wipe Shared `schemas` / `payloads` / `processed` Containers Before/After Each Test Run**
* **Option 3: Launch a Separate Docker Azurite Child Process from `AzuriteFixture`**

## Decision Outcome

Chosen option: **"Option 1: Per-Run Unique Ephemeral Container Names (`test-{runId}-*`) with `DisposeAsync` Deletion and Stale Container Sweeps"**, combined with clean fixture rollback in `BlobSchemaRegistry`:

1. **Per-Run Ephemeral Test Containers ([`AzuriteFixture.cs`](../../test/EstateDataCollection.Tests.Integration/Fixtures/AzuriteFixture.cs)):**
   - Connects to an existing Azurite instance (`AZURE_STORAGE_CONNECTION_STRING`, `ConnectionStrings__BlobStorage`, or default `127.0.0.1:10000` for local Docker, Dev Containers, and GitHub Actions service containers) without shelling out to external `docker` processes.
   - Each `AzuriteFixture` instance generates a 12-character hex run identifier (`_runId`) and binds `BlobSchemaRegistryOptions` / `BlobSubmissionRepositoryOptions` in `IntegrationEndpointHarness` to isolated container names:
     - `test-{runId}-schemas`
     - `test-{runId}-draft-schemas`
     - `test-{runId}-payloads`
     - `test-{runId}-processed`
   - Shared local development containers (`schemas`, `draft-schemas`, `payloads`, `processed`) are never touched by integration tests.
   - `DisposeAsync` deletes all four run-scoped containers on test collection completion (`DeleteIfExistsAsync`), and startup executes `CleanupStaleTestContainersAsync(TimeSpan.FromHours(1))` matching `TestContainerPattern()` (`^test-[0-9a-f]{12}-(schemas|draft-schemas|payloads|processed)$`) to sweep orphaned containers from aborted runs.
2. **Fixture Blob Cleanup & Rollback ([`BlobSchemaRegistry.cs`](../../src/EstateDataCollection.Infrastructure/Registry/BlobSchemaRegistry.cs)):**
   - When `UpdateSchemaTestsAsync` clears a fixture set (`Count == 0`), existing `valid.fixtures.json` / `invalid.fixtures.json` blobs are explicitly deleted (`DeleteIfExistsAsync`); if writing updated fixture blobs fails partway through, previously written fixture blobs are rolled back.

### Consequences

* Good, because developers can keep local `func start` schemas and payloads in Azurite while running `dotnet test` repeatedly with zero cross-contamination.
* Good, because Azurite retains zero leftover `test-*` containers after normal test runs, while orphaned containers from killed processes are self-cleaned after one hour.
* Good, because integration tests work cleanly inside Dev Containers and GitHub Actions service containers without requiring Docker-in-Docker socket access.
* Good, because verification and test runs (`RunSchemaTestsUseCase` / `VerifySchemaUseCase`) run purely in-memory against validation services without writing temporary payload blobs into `payloads` or `processed`.
* Bad, because developers running tests outside a Dev Container or CI must ensure Azurite is started beforehand (or `AZURE_STORAGE_CONNECTION_STRING` is set).
* Bad, because custom integration tests wiring `BlobSchemaRegistryOptions` or `BlobSubmissionRepositoryOptions` manually must consume container names from `AzuriteFixture` rather than hardcoding defaults.

### Confirmation

Compliance is confirmed via:
- [`IntegrationEndpointHarness.cs`](../../test/EstateDataCollection.Tests.Integration/Builders/IntegrationEndpointHarness.cs) and [`BlobSchemaRegistryIntegrationTests.cs`](../../test/EstateDataCollection.Tests.Integration/BlobSchemaRegistryIntegrationTests.cs) configuring all containers from `AzuriteFixture`.
- Post-test Azurite container inspection confirming `0` remaining `test-*` containers after `dotnet test EstateDataCollection.slnx`.

## More Information

Related ADRs:
* [ADR 0003: Multi-Container Blob Storage Landing Zone](0003-multi-container-blob-storage-landing-zone.md)
* [ADR 0012: Local Developer Orchestration with Azurite](0012-local-developer-orchestration-and-http-specifications.md)
* [ADR 0016: Self-Healing Storage Container Initialisation](0016-self-healing-storage-container-initialisation.md)
