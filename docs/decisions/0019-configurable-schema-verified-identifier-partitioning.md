---
status: "accepted"
sub-status: "implemented"
date: 2026-11-24
decision-makers: ["Chris Kelly"]
---

# 19. Configurable Schema-Verified Identifier Extraction and Blob Partitioning

## Context and Problem Statement

Initially, Estate Data Collection assumed every submission carried a 6-digit school `URN` modeled by legacy `Urn` / `SubmissionEnvelope` types extracted from fixed payload paths (`school.urn`, `urn`) and used to partition processed blobs. However, as the platform expanded to support arbitrary ODCS v3 contracts and JSON Schemas (for example establishment-level `LAEstab` contracts, Trust-level `UKPRN` contracts, or telemetry schemas with no establishment identifier), hardcoding URN-only domain types caused false rejections and rigid storage layouts. (Those legacy `Urn` and `SubmissionEnvelope` types have since been removed completely in favour of `SubmissionIdentifier` and `SchemaIdentifierConfig`.)

At the same time, allowing operators to type arbitrary unverified identifier metadata would break submissions at runtime if the configured JSON Pointer did not exist in the JSON Schema or disagreed on requiredness. And predetermining a silent default property in the Admin UI risked operators accidentally saving contracts with the wrong partition key. How should submission identifiers be configured, verified against schemas, extracted, and used to partition storage?

## Decision Drivers

* **Contract-Driven Partitioning:** Processed submission blobs should partition cleanly by whatever identifier property a schema designates (`{idType}={idValue}/schema={schemaName}/v={schemaVersion}/{submissionId}.json`, defaulting to `urn={urn}/...`), or under `unidentified/` when identifier extraction is intentionally disabled (`None`) or optional and omitted.
* **Compile-Time/Ingest-Time Safety:** Identifier configuration must be verified against the schema's JSON Schema AST during Workbench Dry-Run Verify, Submit, Draft Ingestion, and Schedule operations.
* **Explicit Operator Intent (Zero Hidden Defaults):** Operators must always explicitly select a discovered scalar schema property or enter a JSON Pointer path (or explicitly choose `None`), without pre-determined defaults silently deciding the identifier.
* **Automatic Derivation with Optional Override:** Partition label (`Type`) and schema requiredness (`Required`) should be derived from the configured property rather than duplicating redundant inputs.

## Considered Options

* **Option 1: Schema-Verified `SchemaIdentifierConfig` (`JsonPointer` + Derived `Type` + Schema `Required` / `None`) with Explicit UI Selection and Unified `SubmissionIdentifier`**
* **Option 2: Fixed Dropdown of Known DfE Identifiers (`URN`, `UKPRN`, `LAEstab`) Plus Separate Custom Option**
* **Option 3: Continue Hardcoding Top-Level `/urn` Extraction Across All Schemas**

## Decision Outcome

Chosen option: **"Option 1: Schema-Verified `SchemaIdentifierConfig` with Explicit UI Selection and Unified `SubmissionIdentifier`"**, implemented across Domain, Application, Infrastructure, and Admin UI:

1. **Domain Model ([`SchemaIdentifierConfig.cs`](../../src/EstateDataCollection.Domain/Schema/SchemaIdentifierConfig.cs) & [`SubmissionIdentifier.cs`](../../src/EstateDataCollection.Domain/Submission/SubmissionIdentifier.cs)):**
   - `SchemaIdentifierConfig` captures `JsonPointer` (RFC 6901), `Type` (partition label, defaulting to `"URN"` via `SchemaIdentifierConfig.DefaultUrnType` only when legacy schemas omit explicit config), `Required` (`bool?`), or `Type = "None"` when disabled (`IsDisabled`).
   - `SubmissionIdentifier` validates extracted scalar values cleanly (`Value` + `Type`) without forcing every schema to conform to 6-digit numeric URN rules.
2. **Schema Verification & Runtime Extraction:**
   - [`IdentifierSchemaValidator.cs`](../../src/EstateDataCollection.Application/Validators/IdentifierSchemaValidator.cs) resolves the JSON Pointer against the schema's JSON Schema (`properties`, `$defs`, `allOf`/`anyOf`/`oneOf`, required arrays) and ensures scalar string/integer compatibility and consistent requiredness.
   - [`PayloadIdentifierExtractor.cs`](../../src/EstateDataCollection.Application/Validators/PayloadIdentifierExtractor.cs) extracts the configured property at submission finalisation (`FinaliseSubmissionUseCase`) so `BlobSubmissionInfrastructure` stores processed blobs partitioned by `{idType}={sanitizedValue}/schema={schemaName}/v={schemaVersion}/{submissionId}.json` (or `unidentified/schema={schemaName}/v={schemaVersion}/{submissionId}.json` when disabled or optional and omitted).
3. **Explicit UI Selection ([`IdentifierPropertyDiscovery.cs`](../../src/EstateDataCollection.Admin.UI/Models/IdentifierPropertyDiscovery.cs) & [`IdentifierConfigFieldset.razor`](../../src/EstateDataCollection.Admin.UI/Components/IdentifierConfigFieldset.razor)):**
   - Discovers candidate scalar fields from schema descriptors / ODCS YAML / JSON Schema and populates `<select id="identifierProperty">` alongside a manual JSON Pointer mode (`__custom__`).
   - Always defaults new schemas to explicit manual entry ("Enter JSON Pointer manually" — never pre-selecting `/urn`), shows/hides `#identifier-pointer-group` dynamically based on property selection, auto-derives partition label (e.g. `/establishment/laestab` -> `LAEstab`, `/urn` -> `URN`, `/ukprn` -> `UKPRN`) via `DeriveTypeFromPointerOrNull`, and exposes an optional collapsed override `<details>` for `identifierLabel`.

### Consequences

* Good, because non-URN surveys (such as LAEstab, UKPRN, composite/custom IDs, or identifier-less datasets) are first-class citizens without touching code or keeping redundant `Urn` types.
* Good, because invalid JSON Pointers or object/array identifier targets are rejected at Verify/Save time rather than failing live surveyor submissions.
* Good, because UI forms contain a single property-driven workflow with automatic label and requiredness derivation.
* Bad, because legacy schemas without an explicit `IdentifierConfig` fall back to default `ValidationOptions.UrnJsonPaths` extraction behaviour unless updated during scheduling.

### Confirmation

Compliance is confirmed via:
- Unit tests: [`IdentifierSchemaValidatorTests.cs`](../../test/EstateDataCollection.Tests.Unit/Validators/IdentifierSchemaValidatorTests.cs), [`SubmissionIdentifierTests.cs`](../../test/EstateDataCollection.Tests.Unit/Domain/SubmissionIdentifierTests.cs), and [`IdentifierPropertyDiscoveryTests.cs`](../../test/EstateDataCollection.Tests.Unit/AdminUi/IdentifierPropertyDiscoveryTests.cs).
- Integration tests: [`NonUrnSubmissionPromotionIntegrationTests.cs`](../../test/EstateDataCollection.Tests.Integration/EndToEnd/NonUrnSubmissionPromotionIntegrationTests.cs) and [`ScheduleSchemaEndpointTests.cs`](../../test/EstateDataCollection.Tests.Integration/Admin/ScheduleSchemaEndpointTests.cs).

## More Information

Related ADRs:
* [ADR 0002: Dynamic JSON Schema and Dual YAML/JSON Contract Validation](0002-dynamic-json-schema-contract-validation.md)
* [ADR 0004: Session-Based Draft Ingestion & Schema Pinning](0004-session-based-draft-ingestion-and-schema-pinning.md)
