# Estate Data Collection (EDC) Platform
### Technical Architecture, Dynamic Contracts, Regression Testing & Infrastructure Guide

The **Estate Data Collection (EDC)** platform is a data ingestion and contract governance service built on **.NET 10** for the **Department for Education (DfE) Education Estates Directorate (EED)**. It ingests condition surveys and school estate returns, validates payloads against dynamic versioned contracts (**JSON Schema Draft 2020-12 / Draft-07**, **ODCS v3.2.0 YAML**, plus **JsonLogic** and **CEL** business quality rules), extracts schema-verified establishment identifiers, maintains landing-zone draft workspaces, promotes verified submissions for downstream **Common Data Environment (CDE)** ingestion, and provides a **DfE / GOV.UK SSR Admin Portal** for schema inspection, workbench verification, scheduling, and regression testing.

---

## 1. High-Level System Architecture

The service follows **Clean Architecture** organized around generic **Use Cases** (`IUseCase<TResult, TRequest>`). Domain invariants and business orchestration are completely isolated from transport hosts (`AzureFunctions.Public` and `AzureFunctions.Admin`) and UI rendering (`EstateDataCollection.Admin.UI`).

```mermaid
flowchart TD
    classDef input stroke:#0288d1,stroke-width:4px;
    classDef host stroke:#512da8,stroke-width:4px;
    classDef ui stroke:#00695c,stroke-width:4px;
    classDef app stroke:#388e3c,stroke-width:4px;
    classDef domain stroke:#c2185b,stroke-width:4px;
    classDef infra stroke:#f57c00,stroke-width:4px;
    classDef storage stroke:#5d4037,stroke-width:4px;

    M2M["External Surveyors / M2M Clients"]:::input
    Operator["DfE Data / Estates Operators"]:::input
    EG["Azure Event Grid<br/>(BlobCreated on draft-schemas)"]:::input

    subgraph Hosts["Azure Functions v4 (.NET 10 Isolated Worker)"]
        F_PUB["<b>AzureFunctions.Public</b> (:7071)<br/>• /api/schemas*<br/>• /api/submissions*<br/>• /api/about"]:::host
        F_ADM["<b>AzureFunctions.Admin</b> (:7072)<br/>• /api/schemas* &amp; /api/logic/verify<br/>• /api/schemas/tests/run &amp; .../tests<br/>• /manage/* (SSR HTML) &amp; /assets/*<br/>• IngestDraftSchemaEvent"]:::host
    end

    subgraph AdminUI["EstateDataCollection.Admin.UI (Razor Class Library)"]
        RCR["RazorComponentRenderer<br/>Zero-Logic .razor Pages &amp; Components<br/>Presentation ViewModels (Models/ViewModels)"]:::ui
    end

    subgraph Application["Application Layer (IUseCase Handlers)"]
        UC_SUB["CreateSubmission<br/>UpdateSubmission<br/>FinaliseSubmission"]:::app
        UC_SCH["GetSchemas / GetSchemaDetail<br/>VerifySchema / SubmitSchema<br/>ScheduleSchema / IngestDraftSchema"]:::app
        UC_TST["UpdateSchemaTests<br/>RunSchemaTests"]:::app
    end

    subgraph Domain["Domain Layer (Enterprise Invariants)"]
        D_SCH["SchemaMetadata &amp; State Machine<br/>SchemaIdentifierConfig<br/>InvalidFixtureParser &amp; Outcomes"]:::domain
        D_SUB["SubmissionSession<br/>SubmissionIdentifier<br/>SubmissionEnvelope"]:::domain
    end

    subgraph Infrastructure["Infrastructure Layer (Adapters)"]
        VAL["JsonSchemaNetValidator<br/>OdcsContractParser (v3.2.0)<br/>JsonLogicQualityEngine &amp; CelQualityEngine<br/>IdentifierSchemaValidator"]:::infra
        REG["BlobSchemaRegistry (IMemoryCache + Blob)<br/>BlobSubmissionInfrastructure"]:::infra
    end

    subgraph Storage["Azure Blob Storage / Azurite"]
        C_DRF[("draft-schemas<br/>Staging Dropzone")]:::storage
        C_SCH[("schemas<br/>Contracts, Rules, Fixtures, Meta")]:::storage
        C_PAY[("payloads<br/>Sessions &amp; Drafts")]:::storage
        C_PRO[("processed<br/>Canonical CDE Store")]:::storage
    end

    M2M --> F_PUB
    Operator --> F_ADM
    M2M --> F_ADM
    EG --> F_ADM

    F_ADM --> AdminUI
    F_PUB --> Application
    F_ADM --> Application

    Application --> Domain
    Application --> Infrastructure
    Infrastructure --> Storage
```

---

## 2. Infrastructure & Storage Topology

Persistence is decoupled across four Azure Blob Storage containers separating draft staging, immutable contract artefacts, active survey scratchpads, and sealed CDE-ready outputs:

```mermaid
graph LR
    classDef draft stroke:#6a1b9a,stroke-width:4px;
    classDef schemas stroke:#fbc02d,stroke-width:4px;
    classDef payloads stroke:#f57c00,stroke-width:4px;
    classDef processed stroke:#00796b,stroke-width:4px;

    subgraph D_Container["Container: draft-schemas"]
        D1["{schemaName}/v{version}/{file}.odcs.yaml|.schema.json<br/>(+ {blob}.error.json on ingestion failure)"]:::draft
    end

    subgraph S_Container["Container: schemas"]
        S1["{schemaName}/v{version}.schema.json (JSON Schema)<br/>{schemaName}/v{version}.rules.json (JsonLogic &amp; CEL)<br/>{schemaName}/v{version}.odcs.yaml (Source ODCS YAML)"]:::schemas
        S2["{schemaName}/v{version}.meta.json (State, Dates, IdentifierConfig, LastTestRun*)<br/>{schemaName}/v{version}.valid.json &amp; .invalid.json (Test Fixtures)"]:::schemas
    end

    subgraph P_Container["Container: payloads (Scratchpad Landing Zone)"]
        P1["{submissionId}/session.json (Pinned Version, IdentifierConfig, Status)<br/>{submissionId}/draft.json (Iterative Survey JSON)"]:::payloads
    end

    subgraph Pr_Container["Container: processed (Canonical CDE Store)"]
        Pr1["{idType}={idValue}/schema={name}/v={version}/{submissionId}.json<br/>or unidentified/schema={name}/v={version}/{submissionId}.json"]:::processed
    end
```

### Container Directory Conventions & Responsibilities

| Container | Blob Path Pattern(s) | Responsibility | Lifecycle |
| :--- | :--- | :--- | :--- |
| **`draft-schemas`** | `{file}.odcs.yaml`, `{file}.schema.json`, `{file}.error.json` | Event Grid-watched dropzone for draft contract ingestion; writes diagnostic `.error.json` alongside failed drops. | **Staging** |
| **`schemas`** | `{schemaName}/v{version}.schema.json`<br/>`{schemaName}/v{version}.rules.json`<br/>`{schemaName}/v{version}.odcs.yaml`<br/>`{schemaName}/v{version}.meta.json`<br/>`{schemaName}/v{version}.valid.json`<br/>`{schemaName}/v{version}.invalid.json` | Immutable schema contracts, compiled quality rules, identifier metadata, test run telemetry, and mutable golden regression test fixture arrays. | **Contracts Immutable** once Validated/Scheduled/Active; **Test fixtures mutable** via verified `UpdateSchemaTests` |
| **`payloads`** | `{submissionId}/session.json`<br/>`{submissionId}/draft.json` | Holding workspace for active survey sessions and partial draft uploads. | **Temporary**. Deleted on finalisation or Azure Blob lifecycle policies |
| **`processed`** | `urn={urn}/schema={name}/v={v}/{id}.json`<br/>`{partitionLabel}={value}/...`<br/>`unidentified/schema={name}/v={v}/{id}.json` | Sealed payloads tagged `ReadyForCde` with SHA-256 hash and audit metadata, partitioned by configured `SchemaIdentifierConfig` ([ADR 0019](docs/decisions/0019-configurable-schema-verified-identifier-partitioning.md)). | **Permanent Canonical Record** |

---

## 3. Three-Stage Validation & Ingestion Pipeline

Payloads submitted via the Public API and fixtures evaluated during contract verification/regression testing share the exact same three-stage validation pipeline:

1. **Stage 1 — Structural Contract (`JsonSchemaNetValidator`):** Evaluates JSON Schema Draft 2020-12 or Draft-07 (including compiled ODCS v3.2.0 schema definitions).
2. **Stage 2 — Business Logic / Quality Rules (`JsonLogicValidator`):** Executes attached **JsonLogic** and **CEL (`CelQualityEngine`)** rules (`severity`, `dimension`, `id`, `path`, `message`).
3. **Stage 3 — Verified Establishment Identifier (`SubmissionIdentifier` + `IdentifierSchemaValidator`):**
   - Each schema version stores a `SchemaIdentifierConfig` (`Type` partition label, `JsonPointer` e.g. `/urn` or `/school/urn`, `Required` derived from JSON Schema AST, or `Type = "None"` to disable).
   - Validated at schema author/schedule time via `IdentifierSchemaValidator` so non-existent or non-scalar pointers are rejected before activation.

```mermaid
flowchart TD
    classDef route stroke:#0288d1,stroke-width:4px;
    classDef check stroke:#f57c00,stroke-width:4px;
    classDef blob stroke:#7b1fa2,stroke-width:4px;
    classDef action stroke:#388e3c,stroke-width:4px;

    subgraph Step1["Step 1: Session Handshake & Version Pinning"]
        R1["POST /api/submissions/{schemaName}?version=x"]:::route --> A1["Resolve Active or Explicit Version"]:::action
        A1 --> B1[("payloads/{submissionId}/session.json<br/>(Pins Version + IdentifierConfig)")]:::blob
        B1 --> RESP1["201 Created (submissionId, pinnedVersion)"]:::action
    end

    subgraph Step2["Step 2: Iterative Draft Saves (Non-blocking)"]
        R2["PUT /api/submissions/{submissionId}"]:::route --> A2["Fetch pinned schema, rules &amp; identifier config"]:::action
        A2 --> V2["Run Schema + JsonLogic/CEL + Identifier inspection"]:::action
        V2 --> B2[("payloads/{submissionId}/draft.json")]:::blob
        B2 --> RESP2["200 OK (isValid, pendingErrors[])"]:::action
    end

    subgraph Step3["Step 3: Strict Finalisation & Partitioned Promotion"]
        R3["POST /api/submissions/{submissionId}/finalise"]:::route --> C3{"Schema, Rules &amp;<br/>Required Identifier valid?"}:::check
        C3 -- "NO" --> ERR3["422 Unprocessable Entity<br/>(Draft remains in payloads/)"]:::check
        C3 -- "YES" --> A3["Compute SHA-256 &amp; resolve partition key<br/>({idType}={idValue} or unidentified)"]:::action
        A3 --> B3[("processed/{partition}/schema=.../{id}.json")]:::blob
        B3 --> DEL3["Delete draft.json"]:::action
        DEL3 --> RESP3["200 OK (ReadyForCde)"]:::action
    end

    Step1 --> Step2 --> Step3
```

---

## 4. Schema Lifecycle, Verification & Stored Regression Tests

Schemas support ODCS v3.2.0 YAML contracts or raw JSON Schema + companion `.rules.json` arrays and progress through an immutable lifecycle state machine:

```mermaid
stateDiagram-v2
    direction TB

    [*] --> Draft : Candidate uploaded / Workbench Verify
    Draft --> Validated : Syntax, rules, identifier &amp; fixtures pass
    Draft --> DraftFailed : Verification error (422)

    Validated --> Scheduled : ScheduleSchema (EffectiveFromUtc > Now)
    Validated --> Active : ScheduleSchema / SubmitSchema (EffectiveFromUtc <= Now)

    Scheduled --> Active : System clock passes EffectiveFromUtc
    Active --> Deprecated : Superseded or Deprecated via ScheduleSchema
    Deprecated --> Retired : Cut-off date reached or Retired (409 on edits)
    Retired --> [*]
```

### Stored Regression Test Suites & Soft Warnings ([ADR 0020](docs/decisions/0020-schema-regression-test-suites-and-soft-warnings.md) & [ADR 0022](docs/decisions/0022-authoring-schema-test-fixtures-and-expected-errors.md))

- **Live Test Runs (`RunSchemaTestsUseCase` / `POST /api/schemas/tests/run`):** Evaluates all non-retired schemas (or a single schema version) against all three validation stages and persists `LastTestRunAtUtc`, `LastTestRunAppVersion`, `LastTestRunPassed`, `LastTestRunErrors`, `LastTestRunInvalidFixtures`, and `LastTestRunWarnings` onto `SchemaMetadata`.
- **Fixture Mutability (`UpdateSchemaTestsUseCase` / `PUT /api/schemas/{schemaName}/versions/{version}/tests`):** Allows teams to add or update `.valid.json` and `.invalid.json` test suites for existing schema versions after passing full verification—with automatic blob rollback on write failure—without altering the immutable contract.
- **Expected-Error Assertions & Soft Warnings (`InvalidFixtureParser`):**
  - Negative test entries support an annotated wrapper: `{"description": "...", "payload": {...}, "expectedErrors": ["RULE-..."]}`.
  - Assertions match case-insensitive substrings against runtime diagnostics (`[Rule:<Id>]`, `[Schema]`, `[Identifier]`, `[JSON]`). Every token in `expectedErrors` must match.
  - Bare negative payloads without `expectedErrors` remain non-blocking: they pass if they fail validation and emit a **soft warning** (`"Failed — unknown if expected"`) surfacing all triggered error strings so authors can promote them to explicit assertions.

---

## 5. Admin Portal Architecture ([ADR 0017](docs/decisions/0017-ssr-admin-portal-and-proxy-boundary.md) & [ADR 0018](docs/decisions/0018-zero-logic-razor-views-and-presentation-viewmodels.md))

- **Pure Static SSR + Progressive Enhancement:** [`EstateDataCollection.AzureFunctions.Admin`](src/EstateDataCollection.AzureFunctions.Admin) hosts the SSR portal routes (`/manage/*`, `/assets/*`), rendering compliant GOV.UK Frontend / DfE Branding HTML via [`RazorComponentRenderer`](src/EstateDataCollection.Admin.UI/Rendering/RazorComponentRenderer.cs) (`HtmlRenderer`) and serving embedded CSS, fonts, images, and minimal vanilla JS (`workbench.js`) directly from `EstateDataCollection.Admin.UI` via `StaticAssetEndpoint`.
- **Unified In-Process Host (`"routePrefix": ""`):** Admin UI endpoints resolve `IUseCase<TResult, TRequest>` directly in-process inside `EstateDataCollection.AzureFunctions.Admin`, while JSON API routes retain explicit `api/...` route prefixes with `AuthorizationLevel.Function`.
- **Zero-Logic `.razor` Templates:** Every `.razor` page and component binds exclusively to precomputed properties, boolean flags, and child projection records in `EstateDataCollection.Admin.UI/Models/ViewModels/*.cs`. No `@{ ... }` blocks, helper methods, LINQ, URL encoding, or date/JSON formatting exist inside `.razor` templates.
- **Portal Routes:**
  - `GET /manage` — Schema catalogue dashboard with `SchemaSectionCard` test status badges (`Passed`, `Passed (warnings)`, `Failed`, `Not run`) and global `POST /manage/tests/run` trigger.
  - `GET/POST /manage/workbench` & `POST /manage/workbench/verify` — Interactive ODCS/JSON Schema authoring, auto-discovered identifier selection, and dry-run verification.
  - `GET /manage/schemas/{schemaName}/` & `.../details` — Version overview, interactive schema field tree, quality rules viewer, and raw contract preview.
  - `GET/POST /manage/schemas/{schemaName}/{version}/schedule` — Lifecycle window and identifier configuration management.
  - `GET/POST /manage/schemas/{schemaName}/{version}/tests` & `.../tests/run` — Stored fixture viewer, inline test editor, and per-version test execution.

---

## 6. Unified API Response Envelope

Every HTTP API endpoint returns a consistent `ApiResponse<T>` contract (`Successful`, `Data`, and RFC 7807 `ProblemDetails` on failure):

#### Success Pattern (`200 OK` / `201 Created`):
```json
{
  "successful": true,
  "data": {
    "submissionId": "0192e2b4-7f12-7000-a549-39f826bc5411",
    "schemaName": "cdc_survey",
    "version": 1
  }
}
```

#### Failure Pattern (`422 Unprocessable Entity` / `404 Not Found`):
```json
{
  "successful": false,
  "problem": {
    "status": 422,
    "title": "Schema.ContractViolation",
    "detail": "Payload failed schema validation.",
    "errors": {
      "urn": ["Value is 'string' but should be 'integer'"],
      "school_address": ["Required property 'school_address' not found"]
    }
  }
}
```

---

## 7. Key Design Decisions & Guarantees

1. **Zero-Code Contract Evolution ([ADR 0002](docs/decisions/0002-dynamic-json-schema-contract-validation.md)):** Payloads and quality rules (`JsonLogic` / `CEL` / ODCS v3.2.0) are evaluated dynamically at runtime without C# DTO redeployments.
2. **Version Pinning & Contract Immutability ([ADR 0004](docs/decisions/0004-session-based-draft-ingestion-and-schema-pinning.md)):** In-flight survey sessions remain pinned to the schema version and `SchemaIdentifierConfig` active at creation; committed contracts are immutable across versions while regression test suites remain maintainable (`UpdateSchemaTestsUseCase`).
3. **Schema-Verified Identifier Partitioning ([ADR 0019](docs/decisions/0019-configurable-schema-verified-identifier-partitioning.md)):** Identifier pointers and requiredness are verified against the schema AST up front so processed lake partitions stay consistent and explicitly opt-out schemas route cleanly to `unidentified/`.
4. **Zero-Pollution Integration Testing ([ADR 0021](docs/decisions/0021-isolated-ephemeral-azurite-test-containers.md)):** Automated integration suites execute inside unique ephemeral Azurite containers (`test-{runId}-*`) and clean up on disposal.
5. **Landing Zone Defense ([ADR 0003](docs/decisions/0003-multi-container-blob-storage-landing-zone.md)):** Downstream CDE pipelines subscribe strictly to `processed/`; unfinalised drafts remain isolated in `payloads/`.
