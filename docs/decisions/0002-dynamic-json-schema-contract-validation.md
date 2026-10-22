---
status: "proposed"
sub-status: "implemented"
date: 2026-10-21
decision-makers: ["Chris Kelly"]
---

# 2. Dynamic JSON Schema Contract Validation (Draft 2020-12 via JsonSchema.Net)

## Context and Problem Statement

Condition survey contracts change on annual collection cycles across DfE programs (e.g. CDC1, CDC2, Net Zero estate 
evaluations). Hardcoding payload models into C# classes (`class SurveyPayload { ... }`) or backing relational tables 
(SQL schema columns) would require recompiling, re-testing, and deploying code releases every time survey questions, 
validations, or structural requirements change.

During early architectural exploration, contracts defined in YAML (and parsed via `YamlDotNet`) were evaluated, but 
introduced severe performance penalties due to multi-hop parsing (YAML string $\to$ object graph $\to$ JSON string $\to$ 
DOM node) and third-party dependency baggage. Furthermore, invalid schemas or typos introduced by schema authors could 
corrupt production ingestion pipelines if deployed unchecked.

How should EDC validate dynamic, evolving survey payloads at high throughput without code recompilation or schema 
corruption?

## Decision Drivers

* **Zero-Code Contract Evolution (Principle P1):** Data teams must be able to publish new survey versions (`v1`, `v2`, 
  `v3`) directly to the API without backend engineering code changes or deployments.
* **Standards Compliance:** Strict adherence to modern JSON Schema Draft 2020-12 specifications.
* **Pre-Flight Conformance Guardrails:** Ingestion pipelines must be protected from invalid schemas or unintended 
  breaking changes through automated verification prior to persistence.
* **Performance & Zero Transpilation:** Eliminate runtime YAML conversion hops by standardising natively on JSON.

## Considered Options

* **Option 1: Dynamic JSON Schema Draft 2020-12 via `JsonSchema.Net` with Native JSON Contracts**
* **Option 2: Native .NET `System.Text.Json` Schema Validation**
* **Option 3: YAML-Encoded Contracts with Runtime `YamlDotNet` Transpilation**
* **Option 4: Compile-Time C# POCO Generation from JSON Schemas**

## Decision Outcome

Chosen option: **"Option 1: Dynamic JSON Schema Draft 2020-12 via `JsonSchema.Net` with Native JSON Contracts"**, 
because `JsonSchema.Net` provides full specification compliance with Draft 2020-12, operates directly on 
`System.Text.Json.Nodes.JsonNode` without intermediate conversions, and enables dynamic compilation and caching:

1. **Native JSON Without Transpilation**:
   All contract definitions, validation rules, sample fixtures, and submission envelopes are authored and stored as 
   native JSON in Azure Blob Storage (`schemas` container), eliminating YAML parsing hops.
2. **Pre-Flight Candidate Verification (`VerifySchemaUseCase`)**:
   Before a schema is committed to the registry, it must pass a two-phase dry-run evaluation:
   - **Phase 1 (Meta-Schema Validation)**: Validated against the official JSON Schema Draft 2020-12 meta-schema 
     (`MetaSchemas.Draft202012`).
   - **Phase 2 (Golden Fixture Testing)**: Evaluated against provided `SampleValidFixtures` (must pass) and 
     `SampleInvalidFixtures` (must fail).
3. **In-Memory Compilation & Caching**:
   `BlobSchemaRegistry` downloads raw schema contracts from the `schemas` container, compiles them to in-memory 
   `JsonSchema` objects, and caches them via `IMemoryCache` (15-minute sliding expiration).
4. **Runtime Payload Validation**:
   `JsonSchemaNetValidator` evaluates incoming survey JSON payloads against the compiled schema using `OutputFormat.List` 
   to produce structured RFC 7807 problem details with exact JSON pointer locations (`PropertyPath`).

### Consequences

* Good, because new survey contracts, question additions, and deprecations can be introduced in production without code 
  changes or downtime.
* Good, because malformed schemas or contracts that reject known valid samples are blocked before they can ever be 
  stored in the registry.
* Good, because zero-copy streaming directly into `JsonNode` eliminates YAML parsing overhead.
* Good, because validation errors produce structured JSON pointer paths (`/properties/urn`) mapped directly to RFC 7807 
  problem details.
* Bad, because evaluating complex JSON Schemas on massive multi-megabyte payloads incurs CPU cycles (mitigated by 
  pre-compiling schemas and enforcing payload size limits).

### Confirmation

Compliance is confirmed via:
- Unit tests verifying meta-schema evaluation and fixture verification in `VerifySchemaUseCaseTests`.
- Integration tests evaluating payload validation with real schemas in `LocalPipelineEndToEndIntegrationTests`.
- Removal of all YAML dependencies (`YamlDotNet`) from `Directory.Packages.props`.

## Pros and Cons of the Options

### Option 1: Dynamic JSON Schema Draft 2020-12 via JsonSchema.Net (Chosen)

* Good, because it fully supports JSON Schema Draft 2020-12 dialect specifications.
* Good, because it works natively with `System.Text.Json.Nodes.JsonNode`.
* Good, because it provides rich hierarchical error reporting (`OutputFormat.List`).
* Bad, because third-party library maintenance is required.

### Option 2: Native System.Text.Json Schema Validation

* Good, because it is part of the standard .NET runtime.
* Bad, because .NET's built-in schema support lacks complete Draft 2020-12 dialect features and keyword extensions 
  required for complex estate validations.
* Bad, because error output formatting is less flexible than `JsonSchema.Net`.

### Option 3: YAML-Encoded Contracts with YamlDotNet

* Good, because YAML is human-readable and supports inline comments.
* Bad, because multi-hop conversion (YAML $\to$ object $\to$ JSON string $\to$ DOM) causes significant CPU and 
  allocation overhead.
* Bad, because DfE CDE and downstream systems natively consume JSON Schema.

### Option 4: Compile-Time C# POCO Generation

* Good, because it provides compile-time type safety.
* Bad, because it violates Principle P1: any contract change requires recompiling and deploying the application.

## More Information

Artifacts:
* `src/EstateDataCollection.Infrastructure/Validation/JsonSchemaNetValidator.cs`
* `src/EstateDataCollection.Application/UseCases/Schema/VerifySchema/VerifySchemaUseCase.cs`
* `src/EstateDataCollection.Application/UseCases/Schema/SubmitSchema/SubmitSchemaUseCase.cs`
