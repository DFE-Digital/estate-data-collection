---
status: "proposed"
date: 2026-10-21
decision-makers: ["Chris Kelly"]
---

# 6. Self-Contained Deployment Strategy on Azure Functions Flex Consumption

## Context and Problem Statement

The EDC service is built on **.NET 10 (Preview/STS)** to leverage modern C# language features, compile-time performance 
optimisations, and advanced JSON Schema capabilities. However, cloud hosting environments like Azure Functions Flex 
Consumption Linux runtimes may not yet provide .NET 10 as a pre-installed system runtime at general availability.

Deploying as a framework-dependent application on runtimes lacking .NET 10 causes startup crashes or host initialization 
failures. Furthermore, bundle size directly impacts cold-start scale-out latency in serverless environments.

How should the EDC Azure Functions host be packaged and deployed to guarantee runtime compatibility, rapid cold-start 
scaling, and deterministic execution?

## Decision Drivers

* **Runtime Independence:** Deploy .NET 10 applications seamlessly to Azure Functions Flex Consumption without depending 
  on Microsoft host runtime availability.
* **Cold-Start Performance:** Minimise artifact size and startup latency on serverless instance scale-out.
* **Deterministic Deployment Artifacts:** Produce self-contained, reproducible Linux x64 deployment packages.
* **Clean Artifact Packaging:** Exclude local development settings and build debris from production deployment zips.

## Considered Options

* **Option 1: Linux Self-Contained Publishing (`-r linux-x64 --self-contained true`) with `.funcignore` Optimization**
* **Option 2: Framework-Dependent Deployment (FDD)**
* **Option 3: Custom OCI Container Image Deployment**

## Decision Outcome

Chosen option: **"Option 1: Linux Self-Contained Publishing with `.funcignore` Optimization"**, because bundling the 
.NET 10 runtime directly into the deployment package guarantees runtime consistency while avoiding the heavyweight cold 
starts of custom container images:

1. **Self-Contained Linux x64 Build**:
   The deployment package is compiled targeting the specific Linux serverless environment:
   ```bash
   dotnet publish src/EstateDataCollection.AzureFunctions \
     -c Release \
     -r linux-x64 \
     --self-contained true \
     -o publish/
   ```
2. **Packaging Exclusion (`.funcignore`)**:
   Local configuration secrets (`local.settings.json`), debug symbols, HTTP client test scripts (`*.http`), and test 
   project files are explicitly excluded via `.funcignore` to minimise zip archive size.
3. **Optimised Serverless Host Configuration (`host.json`)**:
   HTTP routes are flattened with an empty route prefix (`"routePrefix": "api"`), and unnecessary default logging 
   categories are filtered to reduce overhead during execution.

### Consequences

* Good, because the application runs reliably on any Azure Functions Linux host regardless of host .NET version support.
* Good, because the production bundle is completely reproducible and self-contained.
* Good, because sensitive local files (`local.settings.json`) are prevented from leaking into production zip bundles.
* Bad, because self-contained publishing increases artifact zip size compared to framework-dependent deployments 
  (approximately 60–80 MB).

### Confirmation

Compliance is confirmed via:
- Automated CI build scripts verifying successful `dotnet publish -r linux-x64 --self-contained true`.
- Verification that `local.settings.json` is omitted from publish output.
- Successful dry-run execution on a clean Linux container host.

## Pros and Cons of the Options

### Option 1: Linux Self-Contained Deployment (Chosen)

* Good, because it provides absolute runtime independence.
* Good, because it enables immediate adoption of .NET 10 features.
* Good, because it requires no Docker daemon or container registry management.
* Bad, because zip payload is larger due to bundled runtime DLLs.

### Option 2: Framework-Dependent Deployment (FDD)

* Good, because publish artifacts are smaller (~10 MB).
* Bad, because deployment fails immediately if the target cloud host lacks the .NET 10 runtime.

### Option 3: Custom OCI Container Image Deployment

* Good, because it provides total control over the OS and dependencies.
* Bad, because container cold starts on Azure Functions are significantly slower than zip packages on Flex Consumption.
* Bad, because managing container registries (ACR) adds cost and CI complexity.

## More Information

Configuration:
* `src/EstateDataCollection.AzureFunctions/.funcignore`
* `src/EstateDataCollection.AzureFunctions/host.json`
