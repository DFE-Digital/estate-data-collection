---
status: "accepted"
sub-status: "implemented"
date: 2026-11-17
decision-makers: ["Chris Kelly"]
---

# 20. Stored Schema Regression Test Suites, Expected-Error Assertions, and Soft Warnings

## Context and Problem Statement

Schema contracts in Estate Data Collection combine JSON Schema definitions, Quality/Business Rules (`JsonLogic` and `CEL`), and `SchemaIdentifierConfig`. Application upgrades (such as validator package updates, engine changes, or identifier pipeline modifications) can subtly alter how live contracts behave against real payloads, while authoring contracts without stored golden fixtures leaves no repeatable regression signal across deployments.

Additionally, storing plain negative ("invalid") JSON fixtures only proves that a payload failed *for some reason* — a payload intended to verify business rule `RULE-CONDITION-GRADE-D` could fail due to an unrelated typo and silently pass a naive "must fail" check. Conversely, rejecting all legacy bare invalid fixtures without `expectedErrors` as hard failures would break existing contracts before operators curate expected assertions. How should stored schema test suites be persisted, edited, executed, and evaluated?

## Decision Drivers

* **Contract Immutability with Mutable Test Coverage:** Adding or updating test fixtures must verify against the immutable schema contract without mutating or re-versioning the schema/rules themselves.
* **Full Pipeline Fidelity:** Both stored tests and dry-run verification must exercise JSON Schema validation, attached JsonLogic/CEL quality rules, and identifier extraction together.
* **Actionable Diagnostics for Negative Fixtures:** Operators must see the exact errors triggered by each invalid fixture, assert expected rule IDs / paths / substrings when known, and receive a non-blocking warning when an invalid fixture lacks `expectedErrors`.
* **Build & Periodic Check Compatibility:** Test runs must record app build version, pass/fail status, outcomes, and warnings back into `SchemaMetadata` for auditability.

## Considered Options

* **Option 1: Stored Per-Version Fixtures + Full-Pipeline Execution + Optional `{ payload, expectedErrors }` Wrapper with Non-Blocking Soft Warnings**
* **Option 2: Separate Offline Unit Test Files in Git Only (No Stored Fixtures in Blob Storage)**
* **Option 3: Hard-Fail All Invalid Fixtures That Omit `expectedErrors`**

## Decision Outcome

Chosen option: **"Option 1: Stored Per-Version Fixtures + Full-Pipeline Execution + Optional `{ payload, expectedErrors }` Wrapper with Non-Blocking Soft Warnings"**, implemented as follows:

1. **Blob-Persisted Test Fixtures & Run History:**
   - Valid (`valid.fixtures.json`) and invalid (`invalid.fixtures.json`) fixture arrays are stored alongside immutable schema artefacts in the `schemas` container (`BlobSchemaRegistry`), with last execution telemetry (`LastTestRunAtUtc`, `LastTestRunAppVersion`, `LastTestRunPassed`, `LastTestRunErrors`, `LastTestRunInvalidFixtures`, `LastTestRunWarnings`) persisted on `SchemaMetadata`.
2. **Use Cases & Endpoints:**
   - [`RunSchemaTestsUseCase`](../../src/EstateDataCollection.Application/UseCases/Schema/RunSchemaTests/RunSchemaTestsUseCase.cs) (`POST /admin/schemas/tests/run`, Admin UI "Run All Schema Tests" & per-version "Run Schema Tests") evaluates all non-retired schemas (or a targeted schema/version) against `VerifySchemaAsync`, exercising JSON Schema, JsonLogic/CEL business rules, and identifier extraction.
   - [`UpdateSchemaTestsUseCase`](../../src/EstateDataCollection.Application/UseCases/Schema/UpdateSchemaTests/UpdateSchemaTestsUseCase.cs) (`PUT /admin/schemas/{schemaName}/{version}/tests`, managed via [`SchemaTestsPage.razor`](../../src/EstateDataCollection.Admin.UI/Pages/SchemaTestsPage.razor)) verifies candidate fixtures against the immutable schema before saving, rolling back fixture blobs on write failure.
3. **Structured Invalid Fixtures, Outcomes & Soft Warnings:**
   - [`InvalidFixtureParser`](../../src/EstateDataCollection.Domain/Schema/InvalidFixtureParser.cs) accepts either a bare payload `{...}` or a structured wrapper `{"description": "...", "payload": {...}, "expectedErrors": ["RULE-ID", ...]}`.
   - [`InvalidFixtureOutcome`](../../src/EstateDataCollection.Domain/Schema/InvalidFixtureOutcome.cs) captures `TriggeredErrors`, `ExpectedErrors`, `UnmatchedExpectedErrors`, `FailedAsExpected`, and `Warning`.
   - **Hard failures:** An invalid fixture unexpectedly passes validation (`TriggeredErrors.Count == 0`) or fails to trigger any entry in `expectedErrors` (`UnmatchedExpectedErrors.Count > 0`).
   - **Non-blocking soft warning:** An invalid fixture fails validation when `expectedErrors` is omitted or empty; the suite still passes (`Passed = true`), surfaces all triggered errors in `VerifyResultPanel` and `SchemaTestsPage`, and tags the fixture `"Failed — unknown if expected"` so operators can promote assertions incrementally.

### Consequences

* Good, because deployments and periodic checks can verify all live contracts in one click or API call (`RunSchemaTestsUseCase`) with full business-rule coverage (`JsonLogic` & `CEL`).
* Good, because `expectedErrors` prevents false-positive negative tests while legacy bare fixtures still run with transparent visibility instead of breaking builds.
* Good, because `SchemaTestsPage` provides an interactive workbench to inspect triggered errors, configure assertions, and re-verify stored tests safely.
* Bad, because `SchemaMetadata` grows slightly to retain per-fixture outcomes and warning messages from the latest run.

### Confirmation

Compliance is confirmed via:
- [`InvalidFixtureParserTests.cs`](../../test/EstateDataCollection.Tests.Unit/Domain/InvalidFixtureParserTests.cs)
- [`RunSchemaTestsUseCaseTests.cs`](../../test/EstateDataCollection.Tests.Unit/UseCases/RunSchemaTestsUseCaseTests.cs) & [`UpdateSchemaTestsUseCaseTests.cs`](../../test/EstateDataCollection.Tests.Unit/UseCases/UpdateSchemaTestsUseCaseTests.cs)
- [`BlobSchemaRegistryVerifyTests.cs`](../../test/EstateDataCollection.Tests.Unit/Infrastructure/BlobSchemaRegistryVerifyTests.cs)
- [`UpdateSchemaTestsEndpointTests.cs`](../../test/EstateDataCollection.Tests.Integration/Admin/UpdateSchemaTestsEndpointTests.cs)
- [`ViewModelPresentationTests.cs`](../../test/EstateDataCollection.Tests.Unit/AdminUi/ViewModelPresentationTests.cs)

## More Information

Related ADRs:
* [ADR 0002: Dynamic JSON Schema Validation](0002-dynamic-json-schema-contract-validation.md)
* [ADR 0015: Calendar Versioning (CalVer) and Log Enrichment](0015-calendar-versioning-calver-and-log-enrichment.md)
* [ADR 0021: Isolated Ephemeral Azurite Containers & Automatic Test Storage Cleanup](0021-isolated-ephemeral-azurite-test-containers.md)
