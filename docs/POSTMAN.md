# Postman Development & Test Kit

This directory contains the official Postman collection and environment configurations for testing, debugging, and automating requests against the **Estate Data Collection (EDC)** platform across both the **Public/External Function App** (`baseUrl`) and the **Internal Admin Function App** (`adminBaseUrl`).

## Files in This Kit

| File | Description |
| :--- | :--- |
| **[`EstateDataCollection.postman_collection.json`](./postman/EstateDataCollection.postman_collection.json)** | Main Postman collection (Format v2.1.0) with automated test scripts, multi-engine verification, ODCS v3.2.0 contracts, and state chaining. |
| **[`EstateDataCollection.Local.postman_environment.json`](./postman/EstateDataCollection.Local.postman_environment.json)** | Environment for local development (`http://localhost:7071/api` for Public API, `http://localhost:7072/api` for Admin API). |
| **[`EstateDataCollection.Dev.postman_environment.json`](./postman/EstateDataCollection.Dev.postman_environment.json)** | Environment for Azure Development Function Apps. |

## Collection Structure & Features

The collection is organized into seven logical folders reflecting the EDC Public and Admin domains:

### `0. Diagnostics & Health (Public API)`
- **`0.1 Get About & Service Version`** (`GET {{baseUrl}}/about`):
  - Anonymous health check.
  - Verifies service name, CalVer version (`version` / `informativeVersion`), Git commit `sha`, and runtime `environment`.

### `1. Admin Verification & Dry-Run (Admin API)`
- **`1.1 Verify Schema Candidate (Draft 2020-12 & Fixtures)`** (`POST {{adminBaseUrl}}/schemas/verify`):
  - Tests schema syntax against JSON Schema Draft 2020-12 / Draft-07 and executes validation assertions against sample valid and invalid fixtures without writing to Azure Blob Storage.
- **`1.2 Verify Standalone Quality Rules (Direct Array - JsonLogic & CEL)`** (`POST {{adminBaseUrl}}/logic/verify`):
  - Validates standalone `jsonlogic` and `cel` quality rule definitions posted as a raw JSON array.
- **`1.3 Verify Multi-Engine Quality Rules with Fixtures (JsonLogic + CEL)`** (`POST {{adminBaseUrl}}/logic/verify`):
  - Evaluates both `jsonlogic` and `cel` quality rules against valid and invalid golden fixtures in a dry-run.
- **`1.4 Verify Paired Draft-07 Schema & Quality Rules (Route A)`** (`POST {{adminBaseUrl}}/schemas/verify`):
  - Tests Route A (`schemaJson` + `rulesJson`) exported from ODCS contracts alongside sample fixtures.
- **`1.5 Verify ODCS v3.2.0 Contract via Raw YAML (Route B)`** (`POST {{adminBaseUrl}}/schemas/verify`):
  - Posts a raw ODCS v3.2.0 YAML `DataContract` (`Content-Type: application/yaml`), compiling it to Draft-07 JSON Schema and extracting embedded quality rules.
- **`1.6 Verify ODCS v3.2.0 Contract via JSON Envelope & Fixtures (Route B)`** (`POST {{adminBaseUrl}}/schemas/verify`):
  - Verifies an `odcsYaml` contract wrapped in JSON alongside `sampleValidFixtures` and `sampleInvalidFixtures`.

### `2. Admin Schema Ingestion, Scheduling & Catalogue (Admin API)`
- **`2.1 Submit Schema v1 with Companion Rules (Unscheduled 'Validated')`** (`POST {{adminBaseUrl}}/schemas/{{schemaName}}/versions/1`):
  - Commits Schema v1 and companion `rulesJson` to the `schemas` container in `Validated` (unscheduled) state.
- **`2.2 Submit Schema v2 (Evolution - Unscheduled 'Validated')`** (`POST {{adminBaseUrl}}/schemas/{{schemaName}}/versions/2`):
  - Demonstrates schema evolution (`school_capacity` removed) stored initially as `Validated` (unscheduled).
- **`2.3 Submit ODCS v3.2.0 Contract via Raw YAML (edc-survey-sites v1)`** (`POST {{adminBaseUrl}}/schemas/{{odcsSiteSchemaName}}/versions/1`):
  - Directly ingests an ODCS v3.2.0 YAML contract (`Content-Type: application/yaml`), persisting `.schema.json`, `.rules.json`, `.odcs.yaml`, and `.meta.json`.
- **`2.4 Get Admin Schema Catalogue (Includes Unscheduled & Retired)`** (`GET {{adminBaseUrl}}/schemas?includeUnscheduled=true&includeFields=true`):
  - Lists all schemas in the registry, including unscheduled (`Validated` / `Draft`) and `Retired` versions.
- **`2.5 Get Admin Schema Version Detail (Includes Raw JSON Schema & Rules)`** (`GET {{adminBaseUrl}}/schemas/{{schemaName}}/versions/1?includeUnscheduled=true&includeRawJsonSchema=true`):
  - Returns full version metadata, extracted field descriptors and constraints, quality rule summaries, and raw JSON Schema.
- **`2.6 Schedule / Activate Schema v1 (edc_payload)`** (`PATCH {{adminBaseUrl}}/schemas/{{schemaName}}/versions/1/schedule`):
  - Assigns `effectiveFromUtc` and `effectiveToUtc` (`lifecycleAction: "Schedule"`) so Schema v1 transitions to `Active`.
- **`2.7 Schedule / Activate ODCS Schema v1 (edc-survey-sites)`** (`PATCH {{adminBaseUrl}}/schemas/{{odcsSiteSchemaName}}/versions/1/schedule`):
  - Activates the `edc-survey-sites` v1 contract.
- **`2.8 Schedule Schema v2 for Future Window`** (`PATCH {{adminBaseUrl}}/schemas/{{schemaName}}/versions/2/schedule`):
  - Schedules Schema v2 for a future effective window.
- **`2.9 Deprecate Schema v2 (Lifecycle Action)`** (`PATCH {{adminBaseUrl}}/schemas/{{schemaName}}/versions/2/schedule`):
  - Transitions Schema v2 to `Deprecated` via `lifecycleAction: "Deprecate"` (also supports `"Retire"`).

### `3. Public Schema Discovery & Catalogue (Public API)`
- **`3.1 List Publicly Visible Schemas`** (`GET {{baseUrl}}/schemas?includeFields=true`):
  - Returns only scheduled/active schemas (`Active`, `Scheduled`, `Deprecated`) with their `activeVersion`, version summaries, and extracted `activeVersionFields`.
- **`3.2 Get Public Schema Catalogue Entry by Name`** (`GET {{baseUrl}}/schemas/{{schemaName}}?includeFields=true`):
  - Retrieves a single schema's public catalogue entry and active version field tree.
- **`3.3 Get Public Schema Version Detail (Fields, Constraints & Quality Rules)`** (`GET {{baseUrl}}/schemas/{{schemaName}}/versions/1?includeRawJsonSchema=true`):
  - Retrieves extracted field descriptors, validation constraints, quality rule summaries, and optional `rawJsonSchema` for a publicly visible schema version.
- **`3.4 Get Public ODCS Schema Version Detail (edc-survey-sites v1)`** (`GET {{baseUrl}}/schemas/{{odcsSiteSchemaName}}/versions/1?includeRawJsonSchema=true`):
  - Retrieves the compiled ODCS schema fields and extracted `CDC-DQ1-JSONLogic` quality rule summary.

### `4. Submission Lifecycle - Generic EDC Schema (Public API)`
- **`4.1 Create Submission Session (Auto Schedule)`** (`POST {{baseUrl}}/submissions/{{schemaName}}`):
  - Automatically resolves the active schema schedule and captures `submissionId` into Postman variables.
- **`4.2 Create Submission Session (Explicitly Pin Version 1)`** (`POST {{baseUrl}}/submissions/{{schemaName}}?version=1`):
  - Explicitly pins the session to Schema v1.
- **`4.3 Upload Draft 1 (Incomplete - Missing Address)`** (`PUT {{baseUrl}}/submissions/{{submissionId}}`):
  - Persists partial draft to `payloads` container and returns HTTP 200 with `isSuccessful: false` and `pendingErrors`.
- **`4.4 Upload Draft 2 (Invalid Data Type - URN is string)`** (`PUT {{baseUrl}}/submissions/{{submissionId}}`):
  - Tests type mismatch diagnostics on partial saves.
- **`4.5 Attempt Finalise While Incomplete (422 Unprocessable Entity)`** (`POST {{baseUrl}}/submissions/{{submissionId}}/finalise`):
  - Verifies that incomplete drafts are rejected with HTTP 422 on finalisation.
- **`4.6 Upload Draft 3 (Complete & Valid)`** (`PUT {{baseUrl}}/submissions/{{submissionId}}`):
  - Complete payload meeting all Schema v1 and quality rule requirements (`isSuccessful: true`, `pendingErrors: []`).
- **`4.7 Finalise Submission (Success - Promote to Processed)`** (`POST {{baseUrl}}/submissions/{{submissionId}}/finalise`):
  - Verifies payload integrity, stamps SHA-256 hash, promotes blob from `payloads` to `processed`, and updates status to `ReadyForCde`.

### `5. ODCS v3.2.0 Survey Sites Workflow (edc-survey-sites)`
- **`5.1 Create ODCS Survey Sites Submission Session`** (`POST {{baseUrl}}/submissions/{{odcsSiteSchemaName}}?version=1`)
- **`5.2 Upload Invalid ODCS Draft (Fails CDC-DQ1-JSONLogic: updated_at < created_at)`** (`PUT {{baseUrl}}/submissions/{{odcsSubmissionId}}`)
- **`5.3 Upload Valid ODCS Draft (Conforms to Draft-07 Schema & CDC-DQ1-JSONLogic)`** (`PUT {{baseUrl}}/submissions/{{odcsSubmissionId}}`)
- **`5.4 Finalise ODCS Survey Sites Submission`** (`POST {{baseUrl}}/submissions/{{odcsSubmissionId}}/finalise`)

### `6. CDC Survey Two-Step Validation Pipeline (10 Quality Rules)`
- **`6.1 Submit CDC Survey Schema v1 + 10 Quality Rules`** (`POST {{adminBaseUrl}}/schemas/{{cdcSchemaName}}/versions/1`)
- **`6.2 Schedule / Activate CDC Survey Schema v1`** (`PATCH {{adminBaseUrl}}/schemas/{{cdcSchemaName}}/versions/1/schedule`)
- **`6.3 Create CDC Survey Submission Session`** (`POST {{baseUrl}}/submissions/{{cdcSchemaName}}`)
- **`6.4 Upload CDC Draft 1 (Fails Step 1 JSON Schema)`** (`PUT {{baseUrl}}/submissions/{{cdcSubmissionId}}`)
- **`6.5 Upload CDC Draft 2 (Passes Step 1, Fails Step 2 RULE-CONDITION-GRADE-D)`** (`PUT {{baseUrl}}/submissions/{{cdcSubmissionId}}`)
- **`6.6 Upload Complete & Valid CDC Survey Payload`** (`PUT {{baseUrl}}/submissions/{{cdcSubmissionId}}`)
- **`6.7 Finalise CDC Survey Submission`** (`POST {{baseUrl}}/submissions/{{cdcSubmissionId}}/finalise`)

## Environment Variables Reference

| Variable | Description | Local Default | Azure Cloud Example |
| :--- | :--- | :--- | :--- |
| `baseUrl` | Public API base URL | `http://localhost:7071/api` | `https://func-edc-pub-dev-uksouth.azurewebsites.net/api` |
| `adminBaseUrl` | Internal Admin API base URL | `http://localhost:7072/api` | `https://func-edc-admin-dev-uksouth.azurewebsites.net/api` |
| `schemaName` | Generic schema partition key | `edc_payload` | `edc_payload` |
| `cdcSchemaName` | CDC full survey schema key | `cdc_survey` | `cdc_survey` |
| `odcsSiteSchemaName` | ODCS v3.2.0 survey sites schema key | `edc-survey-sites` | `edc-survey-sites` |
| `functionKey` | Public Azure Function key | *(Empty - not required locally)* | `dGhpcy1pcy1hLWtleS4uLg==` |
| `adminFunctionKey` | Admin Azure Function key | *(Empty - not required locally)* | `YW5vdGhlci1hZG1pbi1rZXk=` |
| `submissionId` | Auto-populated by 4.1 / 4.2 | *(Managed dynamically)* | *(Managed dynamically)* |
| `odcsSubmissionId` | Auto-populated by 5.1 | *(Managed dynamically)* | *(Managed dynamically)* |
| `cdcSubmissionId` | Auto-populated by 6.3 | *(Managed dynamically)* | *(Managed dynamically)* |

> [!TIP]
> When testing against Azure environments (**Dev**, **Test**, or **Prod**), obtain your Function Keys from the Azure Portal (`Function App -> App keys` or `Functions -> Manage function keys`) and paste them into `functionKey` and `adminFunctionKey`. If both Public and Admin routes are hosted on a single Function App instance, set `adminBaseUrl` equal to `baseUrl`.

## Running with Postman

### With Postman GUI

1. Open the **Postman** desktop app or Postman Web.
2. Click **Import** (top left).
3. Drag and drop the following files into the import dialog:
  - `EstateDataCollection.postman_collection.json`
  - `EstateDataCollection.Local.postman_environment.json`
  - `EstateDataCollection.Dev.postman_environment.json`
4. In the top-right environment selector, select **EstateDataCollection - Local** (or **Development**).

### With Newman CLI

You can execute the entire test kit sequentially via **Newman** (Postman's CLI runner) in terminal or CI/CD pipelines:

```bash
# Install Newman if not already installed
npm install -g newman

# Execute against environment
newman run docs/postman/EstateDataCollection.postman_collection.json \
  -e docs/postman/EstateDataCollection.Local.postman_environment.json \
  --reporters cli
```
