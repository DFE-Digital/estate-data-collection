# Estate Data Collection (EDC) — Postman Test Kit

This directory contains the official Postman collection and environment configurations for testing, debugging, and 
automating requests against the **Estate Data Collection (EDC)** platform.

## Files in This Kit

| File | Description |
| :--- | :--- |
| **[`EstateDataCollection.postman_collection.json`](./EstateDataCollection.postman_collection.json)** | Main Postman collection (Format v2.1.0) with automated test scripts and state chaining. |
| **[`EstateDataCollection.Local.postman_environment.json`](./EstateDataCollection.Local.postman_environment.json)** | Environment for local development (`http://localhost:7071/api`). |
| **[`EstateDataCollection.Dev.postman_environment.json`](./EstateDataCollection.Dev.postman_environment.json)** | Environment for Azure Development Function App. |

## 1. Quick Start: Importing into Postman

1. Open the **Postman** desktop app or Postman Web.
2. Click **Import** (top left).
3. Drag and drop the following files into the import dialog:
   - `EstateDataCollection.postman_collection.json`
   - `EstateDataCollection.Local.postman_environment.json`
   - (Optional) `EstateDataCollection.Dev.postman_environment.json`
4. In the top-right environment selector, select **EstateDataCollection - Local** (or your target cloud environment).

## 2. Collection Structure & Features

The collection is organized into three logical folders reflecting the EDC domain:

### `0. Diagnostics & Health`
- **`0.1 Get About & Service Version`** (`GET /api/about`):
  - Anonymous health check.
  - Verifies service name, CalVer version (`YYYY.MM.<run>`), Git commit SHA, and runtime environment.

### `1. Schema Management (Data Team & CI)`
- **`1.1 Verify Schema Candidate (Dry-Run)`** (`POST /api/schemas/verify`):
  - Tests schema syntax against JSON Schema Draft 2020-12.
  - Executes validation assertions against sample valid and invalid fixtures without writing to Azure Blob Storage.
- **`1.2 Publish & Schedule Schema v1`** (`POST /api/schemas/{{schemaName}}/versions/1`):
  - Persists and schedules Schema v1 to the `schemas` blob container.
- **`1.3 Publish & Schedule Schema v2`** (`POST /api/schemas/{{schemaName}}/versions/2`):
  - Demonstrates schema evolution / contract versioning.

### `2. Submission Lifecycle (Surveyor Flow)`
- **`2.1 Create Submission Session (Auto Schedule)`** (`POST /api/submissions/{{schemaName}}`):
  - Automatically resolves the active schema schedule.
  - **Dynamic State Capture**: Automatically captures the returned `submissionId` and stores it into the Postman 
    environment/collection variables for downstream requests.
- **`2.2 Create Submission Session (Explicitly Pin Version 1)`** (`POST /api/submissions/{{schemaName}}?version=1`):
  - Explicitly pins the session to Schema v1.
- **`2.3 Upload Draft 1 (Incomplete - Missing Address)`** (`PUT /api/submissions/{{submissionId}}`):
  - Tests partial draft persistence. Persists to the `payloads` landing container and returns HTTP 200 with pending 
    validation errors.
- **`2.4 Upload Draft 2 (Invalid Data Type - URN is string)`** (`PUT /api/submissions/{{submissionId}}`):
  - Tests type mismatch diagnostics on partial saves.
- **`2.5 Upload Draft 3 (Complete & Valid)`** (`PUT /api/submissions/{{submissionId}}`):
  - Complete payload meeting all Schema v1 requirements. Returns HTTP 200 with 0 pending errors.
- **`2.6 Finalise Submission (Success - Promote to Processed)`** (`POST /api/submissions/{{submissionId}}/finalise`):
  - Verifies payload integrity, stamps SHA-256 hash, moves blob from `payloads` to `processed` container, and updates 
    status to `ReadyForCde`.

## 3. Environment Variables Reference

| Variable | Description | Local Default | Azure Cloud Example |
| :--- | :--- | :--- | :--- |
| `baseUrl` | API base URL | `http://localhost:7071/api` | `https://func-edc-dev-uksouth.azurewebsites.net/api` |
| `schemaName` | Target schema partition key | `edc_payload` | `edc_payload` |
| `functionKey` | Azure Function host or function key | *(Empty - not required locally)* | `dGhpcy1pcy1hLWtleS4uLg==` |
| `submissionId` | Auto-populated by session creation | *(Managed dynamically)* | *(Managed dynamically)* |

> [!TIP]
> When testing against Azure environments (**Dev**, **Test**, or **Prod**), obtain your Function Key from the Azure Portal 
> (`Function App -> App keys` or `Functions -> Manage function keys`) and paste it into the `functionKey` environment 
> variable. The collection automatically injects the `x-functions-key: {{functionKey}}` header.

## 4. Running with Postman

### 4.1 With Postman GUI

You can execute the entire test kit sequentially via the Postman GUI.

1. Import the collection & environment into Postman.
2. Select the desired environment from the dropdown menu.
3. Click the "Run" button to execute the tests.

### 4.2 With Newman CLI

You can execute the entire test kit sequentially via **Newman** (Postman's CLI runner) in terminal or CI/CD pipelines:

```bash
# Install Newman if not already installed
npm install -g newman

# Execute against environment
newman run postman/EstateDataCollection.postman_collection.json \
  -e postman/EstateDataCollection.{environment}.postman_environment.json \
  --reporters cli
```
