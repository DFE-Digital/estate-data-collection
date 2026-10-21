# Architecture Decision Records (ADRs)

This directory maintains the architectural decision log for the **Estate Data Collection (EDC)** platform for the **Department for Education (DfE) Education Estates Directorate (EED)**.

We use the [Michael Nygard ADR format](http://thinkrelevance.com/blog/2011/11/15/documenting-architecture-decisions) to capture context, decisions, and consequences.

---

## Index of Decisions

| ADR | Title | Status | Date |
| :--- | :--- | :--- | :--- |
| [**0001**](0001-clean-architecture-and-use-cases.md) | Clean Architecture & Generic Use Case (`IUseCase`) Pattern | `Accepted` | 2026-10 |
| [**0002**](0002-dynamic-json-schema-contract-validation.md) | Dynamic JSON Schema Validation (Draft 2020-12 via JsonSchema.Net) | `Accepted` | 2026-10 |
| [**0003**](0003-multi-container-blob-storage-landing-zone.md) | Multi-Container Blob Storage Landing Zone (`schemas`, `payloads`, `processed`) | `Accepted` | 2026-10 |
| [**0004**](0004-session-based-draft-ingestion-and-schema-pinning.md) | Session-Based Draft Ingestion, Schema Version Pinning & UUIDv7 | `Accepted` | 2026-10 |
| [**0005**](0005-source-generated-instrumentation-and-opentelemetry.md) | Source-Generated Compile-Time Instrumentation (`IInstrumentation`) & OpenTelemetry | `Accepted` | 2026-10 |
| [**0006**](0006-flex-consumption-self-contained-deployment.md) | Self-Contained Deployment Strategy on Azure Functions Flex Consumption | `Accepted` | 2026-10 |
| [**0007**](0007-centralised-configuration-and-strongly-typed-options.md) | Centralised Configuration with Strongly-Typed Options and Azure App Configuration | `Accepted` | 2026-10 |
| [**0008**](0008-testing-strategy-awesomeassertions-nsubstitute-and-fixtures.md) | Testing Strategy with NSubstitute, AwesomeAssertions, and Reusable Fixtures | `Accepted` | 2026-10 |
| [**0009**](0009-central-package-management-and-build-props.md) | Central Package Management (CPM) and Solution-Wide Build Properties | `Accepted` | 2026-10 |
| [**0010**](0010-direct-to-storage-landing-zone-and-user-delegation-sas.md) | Direct-to-Storage Landing Zone with User Delegation SAS and Defender Quarantine | `Accepted` | 2026-10 |
| [**0011**](0011-architectural-marker-interfaces-and-assembly-scanning.md) | Architectural Marker Interfaces and Assembly-Anchored Scrutor Scanning | `Accepted` | 2026-10 |
| [**0012**](0012-local-developer-orchestration-and-http-specifications.md) | Local Developer Orchestration with Azurite and Executable HTTP Specifications | `Accepted` | 2026-10 |
| [**0013**](0013-superseding-relational-document-cache-for-pure-blob-correlation.md) | Superseding Relational SQL Document Cache in Favour of Pure Blob Storage Correlation | `Superseded` | 2026-10 |

---

## Historical Consolidation Note

Decisions initially explored in the historical spikes directory (`docs/adr/_historic`) have been harmonised into the current architectural pattern:
- **Relational vs Serverless Storage**: The initial Dapper / Azure SQL document cache exploration (historic ADR 0002) is recorded in [**ADR 0013**](0013-superseding-relational-document-cache-for-pure-blob-correlation.md) to explain the evolution towards the pure multi-container Blob correlation architecture ([**ADR 0003**](0003-multi-container-blob-storage-landing-zone.md) and [**ADR 0004**](0004-session-based-draft-ingestion-and-schema-pinning.md)).
- **Configuration & Dependency Management**: Centralised configuration via `IOption<T>` and Azure App Configuration is formalised in [**ADR 0007**](0007-centralised-configuration-and-strongly-typed-options.md); Central Package Management (CPM) is formalised in [**ADR 0009**](0009-central-package-management-and-build-props.md).
- **Testing & Local Ergonomics**: The testing methodology (AwesomeAssertions, NSubstitute, Common Fixtures) is documented in [**ADR 0008**](0008-testing-strategy-awesomeassertions-nsubstitute-and-fixtures.md); local developer orchestration and `.http` specification runners are documented in [**ADR 0012**](0012-local-developer-orchestration-and-http-specifications.md).
- **Contract Formats & Validation**: Historic ADRs 0006 and 0007 (evaluating YAML vs JSON and selecting `JsonSchema.Net`) are consolidated within [**ADR 0002**](0002-dynamic-json-schema-contract-validation.md).
- **Security & Large Binary Ingestion**: Direct-to-storage landing zone and User Delegation SAS generation (historic ADR 0008) is formalised in [**ADR 0010**](0010-direct-to-storage-landing-zone-and-user-delegation-sas.md).
- **Layer Abstractions**: Core architectural marker interfaces and assembly scanning (historic ADR 0011) are formalised in [**ADR 0011**](0011-architectural-marker-interfaces-and-assembly-scanning.md).
