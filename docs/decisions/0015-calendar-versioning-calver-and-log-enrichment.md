---
status: "accepted"
sub-status: "implemented"
date: 2026-10-22
decision-makers: ["Chris Kelly"]
---

# 15. Calendar Versioning (CalVer) with Build-Once Artifact Stamping and Log Enrichment

## Context and Problem Statement

In continuous deployment pipelines promoting a single pre-compiled binary package across multiple environments (Development $\to$ Test $\to$ Production), tracing logs, metrics, and distributed traces back to the exact code release is essential for troubleshooting. 

Traditional Semantic Versioning (SemVer: `1.0.0`, `1.0.1`) requires manual version bumps, release tags, or complex GitVersion tooling. Furthermore, in cloud-native serverless environments, querying logs in Azure Monitor without knowing the release date and commit hash complicates incident response.

How should EDC version its build artifacts and automatically inject that version into all application logs, traces, and diagnostics without manual version bumping?

## Decision Drivers

* **Time-Based Traceability (CalVer):** Versions must immediately communicate when the release was built (Year and Month) along with a monotonically increasing counter.
* **Build-Once Integrity:** Version information must be baked into the compiled binaries during the initial CI build so the identical stamped artifact travels across all environments without rebuilding.
* **Automatic Observability Enrichment:** All OpenTelemetry traces, metrics, and `ILogger` console/streaming log lines must automatically include the version without developers manually passing version strings into logging calls.
* **Public Release Introspection:** Provide an unauthenticated metadata endpoint (`GET /api/about`) allowing automated smoke tests and external monitors to verify the live running version.

## Considered Options

* **Option 1: CalVer (`YYYY.MM.<run_number>`) Baked at Build Time via MSBuild `InformationalVersion`**
* **Option 2: Semantic Versioning with GitVersion / Manual Git Tags**
* **Option 3: Dynamic Runtime Environment Variable Injection (`APP_VERSION`)**

## Decision Outcome

Chosen option: **"Option 1: CalVer Baked at Build Time via MSBuild `InformationalVersion`"**, because it provides human-readable release chronology, requires zero manual Git tagging, and embeds the version permanently into the compiled assemblies:

1. **CalVer Format**:
   - Short Version: `YYYY.0M.<github.run_number>` (e.g. `2026.10.42`).
   - Informational Version: `YYYY.0M.<github.run_number>+<git_sha>` (e.g. `2026.10.42+a1b2c3d`).
2. **Build-Time Assembly Stamping**:
   During the CI build step (`setup-and-test`), the version is stamped into assembly attributes:
   ```bash
   dotnet publish src/EstateDataCollection.AzureFunctions \
     -c Release -r linux-x64 --self-contained true \
     /p:Version="$CALVER" \
     /p:AssemblyVersion="$CALVER" \
     /p:FileVersion="$CALVER" \
     /p:InformationalVersion="$CALVER+$SHA"
   ```
3. **In-Process Introspection (`AppVersionProvider`)**:
   A shared static helper in `EstateDataCollection.Common` parses `AssemblyInformationalVersionAttribute` on startup, splitting the CalVer release string from the commit hash.
4. **Three-Tier Automatic Log & Metric Enrichment**:
   - **OpenTelemetry / Azure Application Insights**: `ResourceBuilder` stamps `serviceVersion: calVer` and `service.commit.sha` across all `traces`, `exceptions`, `requests`, and `dependencies` tables (`application_Version` column).
   - **`ILogger` Execution Scope (`VersionLoggingMiddleware`)**: Azure Functions worker middleware opens an `ILogger.BeginScope` containing `AppVersion`, `CommitSha`, and `Environment`, ensuring terminal and streaming logs (`az functionapp log tail`) include version metadata on every line.
   - **Live Metadata Route (`GET /api/about`)**: A lightweight anonymous HTTP trigger in `VersionFunctions` returns the active CalVer, commit SHA, environment, and server timestamp for CI smoke tests.

### Consequences

* Good, because engineers and support teams can immediately tell from a log line (`2026.10.42`) when the deployed build was created.
* Good, because no recompilation or environment variable mutation is needed when promoting artifacts between Dev, Test, and Prod.
* Good, because Kusto queries in Application Insights can filter or aggregate directly on `application_Version`.
* Bad, because local builds run outside GitHub Actions fall back to `"local"` unless `/p:InformationalVersion` is passed explicitly.

### Confirmation

Compliance is confirmed via:
- CI build output verifying `/p:InformationalVersion` is passed to `dotnet publish`.
- Calling `curl http://localhost:7071/api/about` returning the expected JSON metadata payload.
- OpenTelemetry traces in Application Insights showing the populated `application_Version` column.

## Pros and Cons of the Options

### Option 1: CalVer Baked via MSBuild InformationalVersion (Chosen)

* Good, because it requires zero manual maintenance or release tag management.
* Good, because the commit SHA is embedded directly in the assembly metadata.
* Good, because it guarantees artifact immutability across deployment stages.
* Bad, because build numbers depend on GitHub Actions run numbers.

### Option 2: SemVer with Manual Git Tags

* Good, because it is familiar for public open-source libraries.
* Bad, because SaaS services do not have breaking public API version semantics that benefit from SemVer major/minor increments.
* Bad, because it introduces release friction and manual tagging steps.

### Option 3: Dynamic Runtime Environment Variables

* Good, because settings can be changed in Azure Application Settings without redeploying code.
* Bad, because it decouples the binary from its version; an incorrect environment variable creates false telemetry.

## More Information

Implementation:
* `src/EstateDataCollection.Common/AppVersionProvider.cs`
* `src/EstateDataCollection.AzureFunctions/Middleware/VersionLoggingMiddleware.cs`
* `src/EstateDataCollection.AzureFunctions/Routes/VersionFunctions.cs`
* `src/EstateDataCollection.Infrastructure/Monitoring/MonitoringExtensions.cs`
