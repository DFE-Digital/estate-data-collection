# Architecture Decision Records (ADRs)

This directory maintains the architectural decision log for the **Estate Data Collection (EDC)** platform for the **Department for Education (DfE) Education Estates Directorate (EED)**.

We use the [Michael Nygard ADR format](http://thinkrelevance.com/blog/2011/11/15/documenting-architecture-decisions) to capture context, decisions, and consequences.

---

## Index of Decisions

| ADR | Title | Status | Date |
| :--- | :--- | :--- | :--- |
| [**0001**](decisions/0001-clean-architecture-and-use-cases.md) | Clean Architecture & Generic Use Case (`IUseCase`) Pattern | `Accepted` | 2026-10 |
| [**0002**](decisions/0002-dynamic-json-schema-contract-validation.md) | Dynamic JSON Schema and Dual YAML/JSON Contract Validation (JSON-over-YAML Precedence) | `Accepted` | 2026-11 |
| [**0003**](decisions/0003-multi-container-blob-storage-landing-zone.md) | Multi-Container Blob Storage Landing Zone (`schemas`, `payloads`, `processed`) | `Accepted` | 2026-10 |
| [**0004**](decisions/0004-session-based-draft-ingestion-and-schema-pinning.md) | Session-Based Draft Ingestion, Schema Version Pinning & UUIDv7 | `Accepted` | 2026-10 |
| [**0005**](decisions/0005-source-generated-instrumentation-and-opentelemetry.md) | Source-Generated Compile-Time Instrumentation (`IInstrumentation`) & OpenTelemetry | `Accepted` | 2026-10 |
| [**0006**](decisions/0006-flex-consumption-self-contained-deployment.md) | Self-Contained Deployment Strategy on Azure Functions Flex Consumption | `Accepted` | 2026-10 |
| [**0007**](decisions/0007-centralised-configuration-and-strongly-typed-options.md) | Centralised Configuration with Strongly-Typed Options and Azure App Configuration | `Accepted` | 2026-10 |
| [**0008**](decisions/0008-testing-strategy-awesomeassertions-nsubstitute-and-fixtures.md) | Testing Strategy with NSubstitute, AwesomeAssertions, and Reusable Fixtures | `Accepted` | 2026-10 |
| [**0009**](decisions/0009-central-package-management-and-build-props.md) | Central Package Management (CPM) and Solution-Wide Build Properties | `Accepted` | 2026-10 |
| [**0010**](decisions/0010-direct-to-storage-landing-zone-and-user-delegation-sas.md) | Direct-to-Storage Landing Zone with User Delegation SAS and Defender Quarantine | `Accepted` | 2026-10 |
| [**0011**](decisions/0011-architectural-marker-interfaces-and-assembly-scanning.md) | Architectural Marker Interfaces and Assembly-Anchored Scrutor Scanning | `Accepted` | 2026-10 |
| [**0012**](decisions/0012-local-developer-orchestration-and-http-specifications.md) | Local Developer Orchestration with Azurite and Executable HTTP Specifications | `Accepted` | 2026-10 |
| [**0013**](decisions/0013-superseding-relational-document-cache-for-pure-blob-correlation.md) | Superseding Relational SQL Document Cache in Favour of Pure Blob Storage Correlation | `Superseded` | 2026-10 |
| [**0014**](decisions/0014-segregated-multi-workflow-ci-cd-with-composite-actions.md) | Segregated Multi-Workflow CI/CD with Reusable Composite Actions and Gated Promotion | `Accepted` | 2026-10 |
| [**0015**](decisions/0015-calendar-versioning-calver-and-log-enrichment.md) | Calendar Versioning (CalVer) with Build-Once Artifact Stamping and Log Enrichment | `Accepted` | 2026-10 |
| [**0016**](decisions/0016-self-healing-storage-container-initialisation.md) | Self-Healing Storage Container Initialisation on Application Startup | `Proposed` | 2026-10 |
| [**0017**](decisions/0017-ssr-admin-portal-and-proxy-boundary.md) | Server-Side Rendered Admin Portal & Consolidated Admin Host Boundary | `Accepted` | 2026-11 |
| [**0018**](decisions/0018-zero-logic-razor-views-and-presentation-viewmodels.md) | Zero-Logic Razor Views & Presentation ViewModel Pattern | `Accepted` | 2026-11 |
| [**0019**](decisions/0019-configurable-schema-verified-identifier-partitioning.md) | Configurable Schema-Verified Identifier Extraction & Blob Partitioning | `Accepted` | 2026-11 |
| [**0020**](decisions/0020-schema-regression-test-suites-and-soft-warnings.md) | Stored Schema Regression Test Suites, Expected-Error Assertions & Soft Warnings | `Accepted` | 2026-11 |
| [**0021**](decisions/0021-isolated-ephemeral-azurite-test-containers.md) | Isolated Ephemeral Azurite Containers & Automatic Test Storage Cleanup | `Accepted` | 2026-11 |
| [**0022**](decisions/0022-authoring-schema-test-fixtures-and-expected-errors.md) | Authoring Schema Regression Test Fixtures and Expected-Error Assertions | `Accepted` | 2026-11 |

---

## Historical Consolidation Note

Decisions initially explored in the historical spikes directory (`docs/adr/_historic`) have been harmonised into the current architectural pattern:
- **Relational vs Serverless Storage**: The initial Dapper / Azure SQL document cache exploration (historic ADR 0002) is recorded in [**ADR 0013**](decisions/0013-superseding-relational-document-cache-for-pure-blob-correlation.md) to explain the evolution towards the pure multi-container Blob correlation architecture ([**ADR 0003**](decisions/0003-multi-container-blob-storage-landing-zone.md) and [**ADR 0004**](decisions/0004-session-based-draft-ingestion-and-schema-pinning.md)).
- **Configuration & Dependency Management**: Centralised configuration via `IOption<T>` and Azure App Configuration is formalised in [**ADR 0007**](decisions/0007-centralised-configuration-and-strongly-typed-options.md); Central Package Management (CPM) is formalised in [**ADR 0009**](decisions/0009-central-package-management-and-build-props.md).
- **Testing & Local Ergonomics**: The testing methodology (AwesomeAssertions, NSubstitute, Common Fixtures) is documented in [**ADR 0008**](decisions/0008-testing-strategy-awesomeassertions-nsubstitute-and-fixtures.md); local developer orchestration and `.http` specification runners are documented in [**ADR 0012**](decisions/0012-local-developer-orchestration-and-http-specifications.md).
- **Contract Formats & Validation**: Dual JSON + ODCS YAML ingestion (with JSON-over-YAML precedence), Draft-07 / Draft 2020-12 support via `JsonSchema.Net`, and multi-engine quality rules are documented in [**ADR 0002**](decisions/0002-dynamic-json-schema-contract-validation.md).
- **Security & Large Binary Ingestion**: Direct-to-storage landing zone and User Delegation SAS generation (historic ADR 0008) is formalised in [**ADR 0010**](decisions/0010-direct-to-storage-landing-zone-and-user-delegation-sas.md).
- **Layer Abstractions**: Core architectural marker interfaces and assembly scanning (historic ADR 0011) are formalised in [**ADR 0011**](decisions/0011-architectural-marker-interfaces-and-assembly-scanning.md).
- **CI/CD & Versioning**: Segregated workflows and composite actions are recorded in [**ADR 0014**](decisions/0014-segregated-multi-workflow-ci-cd-with-composite-actions.md); CalVer stamping and observability enrichment are recorded in [**ADR 0015**](decisions/0015-calendar-versioning-calver-and-log-enrichment.md).
- **Storage Resilience**: Self-healing storage container initialisation is proposed in [**ADR 0016**](decisions/0016-self-healing-storage-container-initialisation.md); isolated ephemeral test containers and fixture cleanup are recorded in [**ADR 0021**](decisions/0021-isolated-ephemeral-azurite-test-containers.md).
- **Admin UI, Identifiers & Schema Regression Testing**: The consolidated SSR Admin Portal and Admin API host boundary is captured in [**ADR 0017**](decisions/0017-ssr-admin-portal-and-proxy-boundary.md); zero-logic Razor views and presentation ViewModels in [**ADR 0018**](decisions/0018-zero-logic-razor-views-and-presentation-viewmodels.md); configurable schema-verified identifier extraction in [**ADR 0019**](decisions/0019-configurable-schema-verified-identifier-partitioning.md); stored schema regression test suites with `expectedErrors` and soft warnings in [**ADR 0020**](decisions/0020-schema-regression-test-suites-and-soft-warnings.md); and concrete authoring conventions and examples for valid and invalid test fixtures in [**ADR 0022**](decisions/0022-authoring-schema-test-fixtures-and-expected-errors.md).
