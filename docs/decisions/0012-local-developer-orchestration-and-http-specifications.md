---
status: "accepted"
sub-status: "implemented"
date: 2026-10-21
decision-makers: ["Chris Kelly"]
---

# 12. Local Developer Orchestration with Azurite and Executable HTTP Specifications

## Context and Problem Statement

Developing and testing a cloud-native ingestion service that depends on Azure Blob Storage, Azure Functions triggers, and 
complex JSON schemas poses local developer friction:
1. Requiring active Azure subscriptions or cloud credentials for local debugging incurs cloud costs, increases onboarding 
   friction, and risks accidental mutation of cloud environments.
2. In-memory unit tests cannot exercise actual HTTP routing, JSON deserialisation, HTTP status code mapping, or real Azure 
   Storage REST semantics.
3. API documentation (e.g. Swagger UI or static markdown) frequently drifts from actual implementation behavior unless backed 
   by executable specifications.

How should local orchestration, storage emulation, and API verification be structured for developers and automated testing?

## Decision Drivers

* **Zero Cloud Dependency for Local Development:** Developers and CI runners must be able to run and test the complete 
  pipeline locally without an Azure subscription or active internet connection.
* **Dual Authentication Modes:** Seamless fallback between local connection strings (`UseDevelopmentStorage=true`) and 
  Microsoft Entra ID Managed Identity in deployed cloud environments.
* **Executable API Documentation:** Executable `.http` specification files acting as living documentation and manual/automated 
  test runners across modern IDEs.
* **Rapid Developer Onboarding:** Minimal setup steps to get a fully working environment running locally.

## Considered Options

* **Option 1: Azurite Storage Emulator with Dual-Mode Infrastructure and Executable `.http` Specifications**
* **Option 2: Mocking All Storage Interfaces Locally (No Real Emulator)**
* **Option 3: Ephemeral Azure Cloud Resources Provisioned per Developer**

## Decision Outcome

Chosen option: **"Option 1: Azurite Storage Emulator with Dual-Mode Infrastructure and Executable `.http` Specifications"**, 
because it delivers high-fidelity local testing against real Azure Storage REST APIs without cloud dependencies, combined 
with executable HTTP runners:

1. **Local Blob Storage Emulation (Azurite)**:
   - Developers and integration tests run against Azurite (`UseDevelopmentStorage=true` or Docker container on port `10000`).
   - Integration tests execute inside ephemeral `test-{runId}-*` containers via `AzuriteFixture` ([ADR 0021](0021-isolated-ephemeral-azurite-test-containers.md)).
2. **Dual-Mode Infrastructure Authentication**:
   `EstateDataCollection.Infrastructure` automatically detects its runtime environment:
   - **Local Development**: If `AzureWebJobsStorage` or `ConnectionStrings:BlobStorage` contains a connection string (e.g. 
     `UseDevelopmentStorage=true`), it connects directly using account keys.
   - **Deployed Cloud**: If `EDC_STORAGE_BLOB_ENDPOINT` or `EDC_STORAGE_ACCOUNT_NAME` is provided, it uses `DefaultAzureCredential` to authenticate 
     via Microsoft Entra ID Managed Identity.
3. **Executable REST Specifications (`EstateDataCollection.http` & `EstateDataCollection.Admin.http`)**:
   Executable HTTP request suites in `src/EstateDataCollection.AzureFunctions.Public/EstateDataCollection.http` and `src/EstateDataCollection.AzureFunctions.Admin/EstateDataCollection.Admin.http` serve as both living documentation and interactive test runners within modern IDEs (Rider, VS Code REST Client, Visual Studio):
   - **Admin API (`:7072/api`)**: Verification of candidate JSON Schema / ODCS YAML & JsonLogic/CEL rules, versioned schema submission, lifecycle scheduling, and regression test runs.
   - **Public API (`:7071/api`)**: Active schema discovery, creating submission sessions, uploading partial/invalid drafts with RFC 7807 problem details, checking status, and finalising submissions with automated promotion to `processed`.

### Consequences

* Good, because the entire service can be tested and debugged offline without cloud credentials or Azure subscription costs.
* Good, because the `.http` files provide interactive, one-click execution of the entire end-to-end lifecycle directly from 
  the developer's IDE.
* Good, because new developers can clone the repository, run Azurite, start the two Function hosts, and immediately exercise the full workflow.
* Good, because integration tests execute against the exact same Azurite emulator used for local debugging.
* Bad, because developers must run the Azurite emulator (via Docker, npm, or IDE plugin) when running the local Functions hosts.

### Confirmation

Compliance is confirmed via:
- All integration tests passing against local Azurite emulator instances.
- Execution of requests in `EstateDataCollection.http` (`http://localhost:7071/api`) and `EstateDataCollection.Admin.http` (`http://localhost:7072/api`).

## Pros and Cons of the Options

### Option 1: Azurite Storage + Executable .http Specifications (Chosen)

* Good, because tests run against real Azure Storage REST protocol behavior.
* Good, because dual-mode credentials require zero cloud infrastructure costs during local testing.
* Good, because `.http` files are version-controlled alongside code and executable directly in IDEs.
* Bad, because developers must run a local emulator process.

### Option 2: Mocking All Storage Interfaces Locally

* Good, because no emulator processes are required.
* Bad, because mocks cannot detect Azure Storage REST protocol bugs, path formatting issues, or concurrency conflicts.
* Bad, because mock behavior easily diverges from real cloud services.

### Option 3: Ephemeral Azure Cloud Resources

* Good, because testing occurs in a real Azure environment.
* Bad, because provisioning takes minutes, incurs cloud costs, and requires active internet access and credentials.
* Bad, because developer onboarding is delayed by cloud IAM permission setups.

## More Information

Artifacts:
* `src/EstateDataCollection.AzureFunctions.Public/EstateDataCollection.http`
* `src/EstateDataCollection.AzureFunctions.Public/local.settings.example.json`
* `src/EstateDataCollection.AzureFunctions.Admin/EstateDataCollection.Admin.http`
* `src/EstateDataCollection.AzureFunctions.Admin/local.settings.example.json`
* `test/EstateDataCollection.Tests.Integration/`
