---
status: "accepted"
sub-status: "implemented"
date: 2026-11-24
decision-makers: ["Chris Kelly"]
---

# 2. Dynamic JSON Schema and Dual YAML/JSON Contract Validation

## Context and Problem Statement

Condition survey contracts change across annual DfE estate collection cycles (e.g. CDC, school site, elements, evidence & answers). Hardcoding payload models into C# classes or relational table columns would require code recompilation and redeployments every time survey questions, structural schemas, or quality rules change.

Data engineering teams author contracts in two complementary formats depending on tooling and workflow:
1. **Native JSON contracts**: standalone JSON Schema (`*.schema.json`, supporting Draft 2020-12 and Draft-07) paired with standalone quality rule arrays (`*.rules.json` / `*.logic.json` containing **JsonLogic** or **CEL** rules).
2. **ODCS v3.2.0 YAML data contracts** (`*.odcs.yaml` / `*.yaml`): human-readable Open Data Contract Standard definitions containing embedded schema definitions and `quality` rule blocks.

How should EDC ingest, verify, and execute both JSON and YAML contract formats at runtime while maintaining high throughput, unambiguous precedence when both formats are provided, and strict pre-flight validation?

## Decision Drivers

* **Zero-Code Contract Evolution (Principle P1):** Data teams must be able to publish new survey versions (`v1`, `v2`, `v3`) via HTTP APIs, the Admin Workbench, or the `draft-schemas` Blob dropzone without backend code changes.
* **Dual Authoring Ergonomics (JSON + ODCS YAML) with JSON Precedence:** Support both JSON (`*.schema.json`, `*.rules.json`) and ODCS YAML (`*.odcs.yaml` / `*.yaml`) for data teams; when both JSON and YAML are provided or co-located for the same draft contract, **explicit JSON files take precedence over YAML** so teams can supply hand-crafted JSON Schema / rules alongside an ODCS document.
* **Standards & Multi-Engine Compliance:** Evaluate JSON Schema Draft 2020-12 and Draft-07 via `JsonSchema.Net`, plus declarative **JsonLogic** (`JsonLogicQualityEngine`) and **CEL** (`CelQualityEngine`) business quality rules.
* **Pre-Flight Conformance Guardrails:** Block malformed schemas, invalid rule expressions, broken identifier pointers, or failing golden fixtures before schemas enter the active registry.

## Considered Options

* **Option 1: `JsonSchema.Net` (Draft 2020-12 & Draft-07) + Multi-Engine Quality Rules (`JsonLogic` & `CEL`) + Build/Ingest-Time ODCS YAML (`YamlDotNet`) Parser with JSON-Over-YAML Precedence**
* **Option 2: Native JSON Contracts Only (Reject ODCS YAML)**
* **Option 3: Runtime YAML Transpilation on Every Submission Request**
* **Option 4: Compile-Time C# POCO Generation from Schemas**

## Decision Outcome

Chosen option: **"Option 1: `JsonSchema.Net` + Multi-Engine Quality Rules + Ingest-Time ODCS YAML Parser with JSON-Over-YAML Precedence"**:

1. **Dual YAML & JSON Ingestion with JSON-Over-YAML Precedence (`OdcsContractParser`, `BlobSchemaRegistry`, `IngestDraftSchemaUseCase`, `VerifySchemaUseCase`, `SubmitSchemaUseCase`):**
   - Contracts may be submitted as raw JSON (`schemaJson` + optional `rulesJson`), raw ODCS v3.2.0 YAML (`odcsYaml`), or both together.
   - `OdcsContractParser` (backed by `YamlDotNet` with max recursion depth guard `64`) transpiles ODCS v3.2.0 YAML once at verify/submit/ingest time into Draft-07 JSON Schema and extracted `BusinessRule[]` definitions (`jsonlogic` / `cel`) — **never** on live public submission hot paths.
   - **JSON-Over-YAML Precedence:** When both JSON (`schemaJson` / `rulesJson` / `*.schema.json` / `*.rules.json`) and YAML (`odcsYaml` / `*.odcs.yaml`) exist:
     - Explicit `schemaJson` / `*.schema.json` takes precedence over YAML-derived schema JSON.
     - Explicit `rulesJson` / `*.rules.json` (or `*.logic.json`) takes precedence over YAML-extracted quality rules.
     - On `draft-schemas` Blob Created events (`IngestDraftSchemaEvent`), `BlobSchemaRegistry.GetDraftArtefactAsync` probes all companion files for the version prefix (`{prefix}.schema.json`, `.rules.json`/`.logic.json`, `.odcs.yaml`/`.odcs.yml`/`.yaml`/`.yml`, `.meta.json`, `.valid.json`, `.invalid.json`) regardless of which file triggered the event, applies JSON-over-YAML precedence, preserves the source YAML artefact if present, and cleans up all consumed companion blobs together.
2. **Pre-Flight Verification (`VerifySchemaUseCase` & `SchemaVerificationService`):**
   - Validates candidate schema JSON against allowed Draft 2020-12 / Draft-07 meta-schemas (`ValidationOptions.AllowedMetaSchemas`), verifies business rule syntax across enabled engines (`jsonlogic`, `cel`), verifies `SchemaIdentifierConfig` against the schema AST (`IdentifierSchemaValidator`), and runs candidate valid/invalid fixtures (`InvalidFixtureParser`).
3. **In-Memory Caching & Singleton Evaluation Engines:**
   - `BlobSchemaRegistry` caches compiled `JsonSchema`, parsed `BusinessRule` arrays, and `SchemaMetadata` in `IMemoryCache` (15-minute sliding expiration).
   - `JsonSchemaNetValidator`, `JsonLogicQualityEngine`, `CelQualityEngine`, `JsonLogicValidator`, and `OdcsContractParser` are registered as singletons so compiled CEL programs and normalised JsonLogic AST caches are reused across requests.

### Consequences

* Good, because data teams can author contracts in whichever format fits their workflow (ODCS v3.2.0 YAML or raw JSON Schema + rules) without backend changes.
* Good, because JSON-over-YAML precedence prevents stale YAML transpilation from overriding explicit `.schema.json` or `.rules.json` refinements when both files are placed in `draft-schemas` or sent together.
* Good, because YAML parsing occurs only at verify/submit/ingest time; runtime survey validation executes purely against cached JSON Schema ASTs, normalised JsonLogic ASTs, and compiled CEL programs.
* Bad, because ingestion workflows must probe and clean up companion files across extensions (`.schema.json`, `.rules.json`, `.odcs.yaml`) cleanly to avoid partial ingestion races.

### Confirmation

Compliance is confirmed via:
- Unit tests in [`OdcsContractParserTests.cs`](../../test/EstateDataCollection.Tests.Unit/Infrastructure/OdcsContractParserTests.cs), [`QualityRulesEngineTests.cs`](../../test/EstateDataCollection.Tests.Unit/Infrastructure/QualityRulesEngineTests.cs), and [`IngestDraftSchemaUseCaseTests.cs`](../../test/EstateDataCollection.Tests.Unit/UseCases/IngestDraftSchemaUseCaseTests.cs) (explicitly verifying JSON-over-YAML precedence).
- Integration tests in [`DraftBlobIngestionLifecycleIntegrationTests.cs`](../../test/EstateDataCollection.Tests.Integration/EndToEnd/DraftBlobIngestionLifecycleIntegrationTests.cs) verifying companion JSON/YAML probing, precedence, and cleanup against Azurite.

## More Information

Artifacts:
* `src/EstateDataCollection.Infrastructure/Registry/OdcsContractParser.cs`
* `src/EstateDataCollection.Infrastructure/Registry/BlobSchemaRegistry.cs`
* `src/EstateDataCollection.Infrastructure/Validation/JsonSchemaNetValidator.cs`
* `src/EstateDataCollection.Infrastructure/Validation/JsonLogicValidator.cs`
* `src/EstateDataCollection.Application/UseCases/Schema/IngestDraftSchema/IngestDraftSchemaUseCase.cs`
