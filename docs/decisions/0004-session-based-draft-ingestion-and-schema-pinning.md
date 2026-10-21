---
status: "accepted"
date: 2026-10-21
decision-makers: ["Chris Kelly"]
---

# 4. Session-Based Draft Ingestion, Schema Version Pinning, and UUIDv7

## Context and Problem Statement

School condition surveys are complex, multi-section assessments often performed over several days or weeks by surveying 
teams on site. Surveyors must be able to upload incomplete drafts incrementally without being rejected by strict schema 
validation rules designed for final submission.

However, if a new schema version is published by the data team mid-survey, evaluating an in-flight survey against the 
new schema could introduce breaking validation failures for questions that were valid when the survey began. Furthermore,
distributed drafts require globally unique, time-sortable identifiers to prevent collision and enable efficient indexing.

How should the EDC service manage multi-step surveyor draft lifecycles while guaranteeing that schema rules remain 
consistent throughout the survey duration?

## Decision Drivers

* **Surveyor Ergonomics:** Support incremental draft uploads (e.g. saving Room A on Day 1, Room B on Day 2) with 
  constructive validation feedback without blocking draft persistence.
* **Contract Stability (Schema Pinning):** Once a submission session is created, it must remain pinned to its original 
  schema version for its entire lifecycle, immune to subsequent schema releases.
* **Deterministic Finalisation Gate:** Incomplete or invalid payloads must be strictly blocked from finalisation with 
  RFC 7807 problem details.
* **Time-Ordered Uniqueness:** Submission identifiers must be globally unique, collision-free, and chronologically 
  sortable without relational database auto-increment keys.

## Considered Options

* **Option 1: Session-Based Draft Ingestion with Pinned Schema Version and UUIDv7**
* **Option 2: Single-Shot Atomic Submission Only (Reject Incomplete Drafts)**
* **Option 3: Always Validate Against the Latest Active Schema (Dynamic Floating Version)**

## Decision Outcome

Chosen option: **"Option 1: Session-Based Draft Ingestion with Pinned Schema Version and UUIDv7"**, because it provides 
surveyor flexibility while preserving contract integrity:

1. **3-Step Draft Ingestion Lifecycle**:
   - **Step 1: Create Session (`POST /submissions/{schemaName}`)**:
     Resolves the active schema schedule (or honours an explicit `?version=X` query), generates a **UUIDv7** 
     `submissionId`, pins the schema version, and creates a `Draft` metadata envelope.
   - **Step 2: Update Draft (`PUT /submissions/{submissionId}`)**:
     Accepts partial or incomplete JSON payloads. Validates against the pinned schema, persists the draft to the 
     `payloads` container, and returns validation diagnostics with `200 OK` (enabling client feedback while preserving 
     work).
   - **Step 3: Finalise (`POST /submissions/{submissionId}/finalise`)**:
     Enforces strict validation against the pinned schema. If valid, calculates a SHA-256 integrity hash, moves the 
     payload to the `processed` container (`{urn}/{year}/{submissionId}.json`), tags status as `ReadyForCde`, and purges 
     the draft from `payloads`. If invalid, rejects with `422 Unprocessable Entity` and leaves the draft in `payloads`.
2. **Schema Pinning**:
   The `PinnedSchemaVersion` recorded at session creation is immutable. If `v2` becomes active while a survey is in 
   draft, the draft continues evaluating against `v1` until finalised.
3. **UUIDv7 Identifiers**:
   All `submissionId` values use UUIDv7 (RFC 9562). The timestamp-first 48-bit UNIX millisecond prefix ensures 
   chronological sorting in Azure Storage and blob point queries.

### Consequences

* Good, because surveyors can save partial progress without fear of losing data due to incomplete required fields.
* Good, because surveys are protected from mid-collection breaking contract changes via schema version pinning.
* Good, because UUIDv7 provides natural time-ordered blob sorting without database sequences.
* Good, because only 100% valid, verified envelopes ever enter the `processed` canonical storage container.
* Bad, because client applications must manage a multi-step session flow (`POST` create $\to$ `PUT` draft $\to$ `POST` 
  finalise).

### Confirmation

Compliance is confirmed via:
- Unit tests verifying draft creation, update validation feedback, and finalisation rejection in `FinaliseSubmissionUseCaseTests`.
- Integration tests executing the complete 3-step lifecycle against live Azurite storage in `LocalPipelineEndToEndIntegrationTests`.
- Verification of UUIDv7 chronological sorting across consecutive session creations.

## Pros and Cons of the Options

### Option 1: Session-Based Draft Ingestion with Schema Pinning (Chosen)

* Good, because it accurately models real-world multi-day condition survey workflows.
* Good, because schema pinning guarantees predictability for surveyors.
* Good, because RFC 7807 responses guide surveyors on missing or invalid fields.
* Bad, because temporary drafts require scheduled cleanup policies in storage.

### Option 2: Single-Shot Atomic Submission Only

* Good, because the API surface is simple (single `POST` endpoint).
* Bad, because mobile surveyor apps must store complex multi-day state locally on device, risking data loss if devices 
  are damaged or cleared.
* Bad, because surveyors receive no server-side validation feedback until final submission.

### Option 3: Always Validate Against Latest Active Schema

* Good, because all finalised submissions conform to the newest schema.
* Bad, because surveyors could begin a survey under `v1` rules and be unable to submit because an unexpected `v2` release 
  changed required fields.

## More Information

Implementation:
* `src/EstateDataCollection.Domain/Submission/SubmissionEnvelope.cs`
* `src/EstateDataCollection.Application/UseCases/Submissions/CreateSubmission/CreateSubmissionUseCase.cs`
* `src/EstateDataCollection.Application/UseCases/Submissions/UpdateSubmission/UpdateSubmissionUseCase.cs`
* `src/EstateDataCollection.Application/UseCases/Submissions/FinaliseSubmission/FinaliseSubmissionUseCase.cs`
