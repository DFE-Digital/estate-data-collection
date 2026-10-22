---
status: "proposed"
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
* **Executable API Documentation:** Single executable specification file acting as living documentation and manual/automated 
  test runner across modern IDEs.
* **Rapid Developer Onboarding:** Minimal setup steps to get a fully working environment running locally.

## Considered Options

* **Option 1: Azurite Storage Emulator with Dual-Mode Infrastructure and Executable `.http` Specification**
* **Option 2: Mocking All Storage Interfaces Locally (No Real Emulator)**
* **Option 3: Ephemeral Azure Cloud Resources Provisioned per Developer**

## Decision Outcome

Chosen option: **"Option 1: Azurite Storage Emulator with Dual-Mode Infrastructure and Executable `.http` Specification"**, 
because it delivers high-fidelity local testing against real Azure Storage REST APIs without cloud dependencies, combined 
with an executable HTTP runner:

1. **Local Blob Storage Emulation (Azurite)**:
   - Developers and integration tests run against Azurite (`UseDevelopmentStorage=true` or Docker container on port `10000`).
   - The infrastructure layer dynamically initialises required containers (`schemas`, `payloads`, `processed`, `landing`, 
     `cleared`, `quarantine`) automatically if they do not exist.
2. **Dual-Mode Infrastructure Authentication**:
   `EstateDataCollection.Infrastructure` automatically detects its runtime environment:
   - **Local Development**: If `AzureWebJobsStorage` or `BlobStorage` contains a connection string (e.g. 
     `UseDevelopmentStorage=true`), it connects directly using account keys.
   - **Deployed Cloud**: If an endpoint URI or account name is provided, it uses `DefaultAzureCredential` to authenticate 
     via Microsoft Entra ID Managed Identity.
3. **Executable REST Specifications (`EstateDataCollection.http`)**:
   An executable HTTP request suite in `EstateDataCollection.AzureFunctions/EstateDataCollection.http` serves as both living 
   documentation and an automated regression test runner within modern IDEs (Rider, VS Code REST Client, Visual Studio):
   - **Section 1 (Schema Management)**: Verification of candidate schemas, pre-flight dry-runs against valid/invalid fixtures, 
     and versioned schema submission.
   - **Section 2 (Survey Ingestion Workflow)**: Creating submission sessions, uploading partial/invalid drafts with RFC 
     7807 problem details, submitting valid drafts, and finalising submissions with automated promotion to the `processed` 
     container.

### Consequences

* Good, because the entire service can be tested and debugged offline without cloud credentials or Azure subscription costs.
* Good, because the `.http` file provides interactive, one-click execution of the entire end-to-end lifecycle directly from 
  the developer's IDE.
* Good, because new developers can clone the repository, run Azurite, press F5, and immediately exercise the full workflow.
* Good, because integration tests execute against the exact same Azurite emulator used for local debugging.
* Bad, because developers must run the Azurite emulator (via Docker, npm, or IDE plugin) when running the local Functions host.

### Confirmation

Compliance is confirmed via:
- All integration tests passing against local Azurite emulator instances (`LocalPipelineEndToEndIntegrationTests`).
- Azure Functions local startup validating container creation across all six containers.
- Execution of all requests in `EstateDataCollection.http` succeeding against `http://localhost:7071/api`.

## Pros and Cons of the Options

### Option 1: Azurite Storage + Executable .http Specification (Chosen)

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
* `src/EstateDataCollection.AzureFunctions/EstateDataCollection.http`
* `src/EstateDataCollection.AzureFunctions/local.settings.json`
* `test/EstateDataCollection.Tests.Integration/`
