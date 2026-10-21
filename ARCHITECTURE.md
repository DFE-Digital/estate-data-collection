# Estate Data Collection (EDC) Platform
### Technical Architecture, Ingestion Pipeline & Infrastructure Guide

The **Estate Data Collection (EDC)** platform is a machine-to-machine (M2M) ingestion service built on **.NET 10** designed for the **Department for Education (DfE) Education Estates Directorate (EED)**. It ingests condition survey data submitted by surveyors, validates payloads against dynamic versioned contracts, maintains landing-zone draft workspaces, and promotes verified submissions for downstream **Common Data Environment (CDE)** consumption.

---

## 1. High-Level System Architecture

The service follows **Clean Architecture** organized around generic **Use Cases** (`IUseCase<TResult, TRequest>`). This completely isolates the domain and business orchestration from the hosting transport, allowing the application to run interchangeably on **Azure Functions (.NET 10 Isolated Worker)** or **Azure Container Apps (ACA) / Minimal APIs** without code modifications.

```mermaid
flowchart TD
    classDef input stroke:#0288d1,stroke-width:5px;
    classDef internal stroke:#0288d1,stroke-width:5px;
    classDef host stroke:#512da8,stroke-width:5px;
    classDef app stroke:#388e3c,stroke-width:5px;
    classDef domain stroke:#c2185b,stroke-width:5px;
    classDef infra stroke:#f57c00,stroke-width:5px;
    classDef storage stroke:#5d4037,stroke-width:5px;

    Client["External Client<br/><i>or</i><br/>Internal Team"]:::input

    subgraph Presentation["Presentation Layer (Transport Adaptors)"]
        AF["<b>Azure Functions</b><br/><i>Current</i><br/><br/>• SchemaFunctions<br/>• SubmissionFunctions"]:::host
        ACA["<b>Azure Container Apps</b><br/><i>Alternative</i><br/><br/>• Minimal API Endpoints<br/><br/>"]:::host
    end

    subgraph Application["Application Layer (Core Orchestration)"]
        UC_Verify["VerifySchemaUseCase"]:::app
        UC_SubmitSchema["SubmitSchemaUseCase"]:::app
        UC_CreateSub["CreateSubmissionSessionUseCase"]:::app
        UC_UpdateSub["UpdateSubmissionUseCase"]:::app
        UC_FinaliseSub["FinaliseSubmissionUseCase"]:::app
    end

    subgraph Domain["Domain Layer (Enterprise Invariants)"]
        D_Schema["SchemaMetadata<br/>• Invariant state rules<br/>• Temporal scheduling<br/>• Immutability"]:::domain
        D_Session["SubmissionSession<br/>• Pinned schema version<br/>• Status & URN assignment"]:::domain
        D_Envelope["SubmissionEnvelope<br/>• Sealed canonical document<br/>• SHA-256 hash"]:::domain
    end

    subgraph Infrastructure["Infrastructure Layer (External Adaptors)"]
        Validator["JsonSchemaNetValidator<br/>(JsonSchema.Net - Draft 2020-12)"]:::infra
        Registry["BlobSchemaRegistry<br/>(IMemoryCache + Blob Store)"]:::infra
        SubmissionRepo["BlobSubmissionInfrastructure<br/>(Session & Draft Management)"]:::infra
    end

    subgraph Storage["Physical Storage (Azure Storage / Azurite)"]
        C_Schemas[("📁 schemas<br/>Contracts & Metadata")]:::storage
        C_Payloads[("📁 payloads<br/>In-Flight Drafts & Sessions")]:::storage
        C_Processed[("📁 processed<br/>Canonical CDE Landing Zone")]:::storage
    end

    Client --> AF
    Client -.-> ACA
    AF --> Application
    ACA -.-> Application

    Application --> Domain
    Application --> Infrastructure

    Infrastructure --> Storage
```

---

## 2. Infrastructure & Storage Topology

All data persistence is decoupled into three dedicated Azure Blob Storage containers, providing strict isolation between contracts, in-flight work-in-progress drafts, and finalized records.

```mermaid
graph LR
    classDef schemas stroke:#fbc02d,stroke-width:5px;
    classDef payloads stroke:#f57c00,stroke-width:5px;
    classDef processed stroke:#00796b,stroke-width:5px;

    subgraph S_Container["Container: schemas"]
        S1["{schemaName}/v{version}.schema.json<br/>(Raw Draft 2020-12 Contract)"]:::schemas
        S2["{schemaName}/v{version}.meta.json<br/>(State, SHA-256, Schedule Dates)"]:::schemas
    end

    subgraph P_Container["Container: payloads (Scratchpad Landing Zone)"]
        P1["{submissionId}/session.json<br/>(Pinned Schema, State, Dates, URN)"]:::payloads
        P2["{submissionId}/draft.json<br/>(Iterative Incomplete Survey JSON)"]:::payloads
    end

    subgraph Pr_Container["Container: processed (Canonical Gold Store)"]
        Pr1["urn={urn}/schema={name}/v={version}/{submissionId}.json<br/>(Sealed, Validated, ReadyForCde)"]:::processed
    end
```

### Container Directory Conventions & Responsibilities

| Container | Path Pattern | Responsibility | Lifecycle |
|---|---|---|---|
| **`schemas`** | `{schemaName}/v{version}.schema.json`<br/>`{schemaName}/v{version}.meta.json` | Stores official JSON Schema contracts and lifecycle schedules. | **Permanent & Immutable** once active. |
| **`payloads`** | `{submissionId}/session.json`<br/>`{submissionId}/draft.json` | Holding area for active survey sessions and draft uploads. | **Temporary**. Pruned on finalization or by Azure Blob Lifecycle retention policies. |
| **`processed`** | `urn={urn}/schema={name}/v={version}/{submissionId}.json` | Sealed surveys tagged `ReadyForCde` with SHA-256 integrity hash and audit metadata. | **Permanent canonical record** for CDE ingestion. |

---

## 3. End-to-End Survey Ingestion Lifecycle

Surveyors inspect school estates iteratively. The system enables partial uploads with real-time feedback before a strict finalization gate moves the verified survey to `processed/`.

```mermaid
flowchart TD
    classDef route stroke:#0288d1,stroke-width:5px;
    classDef check stroke:#f57c00,stroke-width:5px;
    classDef blob stroke:#7b1fa2,stroke-width:5px;
    classDef action stroke:#388e3c,stroke-width:5px;

    %% Step 1: Session Initiation
    subgraph Step1["Step 1: Session Handshake & Version Pinning"]
        R1["POST /api/submissions/{schemaName}?version=x"]:::route
        R1 --> A1["Resolve Active or Explicit Version"]:::action
        A1 --> B1[("📁 payloads/<br/>{submissionId}/session.json")]:::blob
        B1 --> RESP1["Return 201 Created<br/>(submissionId, pinnedVersion)"]:::action
    end

    %% Step 2: Draft Uploads
    subgraph Step2["Step 2: Iterative Draft Saves (Non-blocking)"]
        R2["PUT /api/submissions/{submissionId}"]:::route
        R2 --> A2["Read session.json & fetch pinned schema"]:::action
        A2 --> V2["Evaluate JSON against pinned contract"]:::action
        V2 --> B2[("📁 payloads/<br/>{submissionId}/draft.json")]:::blob
        B2 --> RESP2["Return 200 OK<br/>(isValid, pendingErrors: [...])"]:::action
    end

    %% Step 3: Finalisation
    subgraph Step3["Step 3: Strict Finalisation & Storage Promotion"]
        R3["POST /api/submissions/{submissionId}/finalise"]:::route
        R3 --> C3{"Is payload 100% complete<br/>& valid?"}:::check
        C3 --"NO"--> ERR3["Return 422 Unprocessable Entity<br/>(Payload remains in payloads/)"]:::check
        C3 --"YES"--> A3["Tag SHA-256 hash & mark ReadyForCde"]:::action
        A3 --> B3[("📁 processed/<br/>urn={urn}/.../{submissionId}.json")]:::blob
        B3 --> DEL3["Delete {submissionId}/draft.json"]:::action
        DEL3 --> RESP3["Return 200 OK<br/>(Finalized with CDE URI)"]:::action
    end

    Step1 --> Step2
    Step2 --> Step3
```

---

## 4. Zero-Code Schema Evolution & Lifecycle

The Data Team can modify, deprecate, and activate schemas dynamically without requiring developer intervention, pull requests, or application deployments.

```mermaid
stateDiagram-v2
    direction TB

    [*] --> Draft : Upload candidate schema
    Draft --> Validated : Syntax & fixtures pass
    Draft --> DraftFailed : Syntax or fixture error (422)

    Validated --> Scheduled : SubmitSchema (EffectiveFromUtc > Now)
    Validated --> Active : SubmitSchema (EffectiveFromUtc <= Now)

    Scheduled --> Active : System clock passes EffectiveFromUtc
    Active --> Deprecated : Newer version activated
    Deprecated --> Retired : Cut-off date reached (409)
    Retired --> [*]
```

### Dynamic Verification Workflow

```mermaid
flowchart LR
    classDef route stroke:#0288d1,stroke-width:5px;
    classDef check stroke:#f57c00,stroke-width:5px;
    classDef blob stroke:#7b1fa2,stroke-width:5px;
    classDef action stroke:#388e3c,stroke-width:5px;

    R_VER["POST /schemas/{name}/versions/{v}/verify"]:::route
    R_VER --> C_SYN{"Validate Draft 2020-12<br/>& Sample Fixtures"}:::check
    C_SYN --"Pass"--> OK_VER["200 OK (Verified)"]:::action
    C_SYN --"Fail"--> ERR_VER["422 ProblemDetails (Errors)"]:::check

    R_SUB["POST /schemas/{name}/versions/{v}"]:::route
    R_SUB --> C_MUT{"Is Existing Version<br/>Mutable?"}:::check
    C_MUT --"No (Active/Scheduled)"--> ERR_MUT["409 Conflict (Immutable)"]:::check
    C_MUT --"Yes (New Version)"--> SAVE["Save Contract & Invalidate Cache"]:::action
    SAVE --> B_SCH[("📁 schemas/<br/>v{v}.schema.json<br/>v{v}.meta.json")]:::blob
```

---

## 5. Unified API Response Envelope

Every endpoint returns a consistent `ApiResponse<T>` contract. Successful responses omit `Problem`, and failure responses omit `Data` while exporting standard RFC 7807 `ProblemDetails`:

#### Success Pattern (`200 OK` / `201 Created`):
```json
{
  "successful": true,
  "data": {
    "submissionId": "0192e2b4-7f12-7000-a549-39f826bc5411",
    "schemaName": "edc_payload",
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

## 6. Key Design Decisions & Guarantees

1. **Zero-Code Evolution (ADR-002)**: Payloads are validated dynamically at runtime using `JsonSchema.Net` (Draft 2020-12) rather than strongly-typed C# DTOs.
2. **Version Pinning**: When a submission session is created, the schema version is stamped on `session.json`. Even if a newer schema version becomes active mid-survey, the surveyor’s in-flight survey remains anchored to its original contract.
3. **Immutability of Active Contracts**: Once a schema version has been committed and marked active/scheduled, it cannot be modified or overwritten. Changes must be registered under version $N+1$.
4. **Landing Zone Defense**: Downstream pipelines (CDE / Silver ETL) subscribe strictly to the `processed` container. Incomplete drafts or invalid payloads in `payloads` are isolated from production consumers.
