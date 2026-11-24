---
status: "accepted"
sub-status: "implemented"
date: 2026-11-24
decision-makers: ["Chris Kelly"]
---

# 17. Server-Side Rendered Admin Portal and Consolidated Admin Host Boundary

## Context and Problem Statement

The Estate Data Collection (EDC) platform required an internal administration portal for Education Estates Directorate operators to inspect schema contracts, preview extracted fields and business rules, dry-run verify ODCS YAML and JSON Schema contracts, manage lifecycle windows, and execute stored regression tests.

Historically, Admin operations were exposed solely as headless HTTP endpoints on [`EstateDataCollection.AzureFunctions.Admin`](../../src/EstateDataCollection.AzureFunctions.Admin) secured by function keys (`x-functions-key`). While an early iteration introduced a separate UI Azure Functions host (`AzureFunctions.AdminUi`) forwarding HTTP proxy calls (`Http*UseCaseProxy`) to the Admin API, operating and deploying three separate Function Apps (Public, Admin API, and Admin UI) added unnecessary operational complexity for the current phase.

At the same time, operators and CI/CD pipelines still benefit from keeping headless JSON API endpoints (`Routes/Api/*`) separate from SSR HTML page routes (`Routes/Admin/*`), while shipping a pure .NET static-SSR portal without separate Node build toolchains or SPA/WASM client complexity. How should the Admin UI and Admin API be structured and hosted today while keeping the option to split into separate Function Apps later?

## Decision Drivers

* **Single Admin Function App for Now (Reversible Split):** Combine the SSR Admin Portal (`Routes/Admin/*`) and Admin Governance API (`Routes/Api/*`) into `EstateDataCollection.AzureFunctions.Admin` to eliminate deploying and managing an extra Function App right now, while keeping route groups and Use Case boundaries cleanly separated if they split in the future.
* **Direct In-Process Use Case Dispatch for UI Pages:** SSR Admin pages (`Routes/Admin/*`) resolve Application `IUseCase<TResult, TRequest>` handlers directly in-process instead of looping HTTP proxy calls back through the same host.
* **Preserve Headless Admin API for CI/HTTP Callers:** Retain `Routes/Api/*` (`/api/schemas*`, Event Grid `IngestDraftSchemaEvent`) protected by `AuthorizationLevel.Function` for automated contract pipelines, schema seeding, and future flow-aware integrations, dropping only duplicate/redundant endpoints (`/api/logic/verify`, superseded by `/api/schemas/verify`).
* **No Separate Frontend Build Toolchain & Clean UI Separation:** Keep Razor components, GOV.UK Frontend / DfE branding assets, and Presentation ViewModels isolated inside the `EstateDataCollection.Admin.UI` Razor Class Library with zero direct Azure Storage SDK dependencies.

## Considered Options

* **Option 1: Consolidate SSR Admin Portal (`Routes/Admin/*`) and Admin API (`Routes/Api/*`) Inside `EstateDataCollection.AzureFunctions.Admin` Using In-Process `IUseCase<TResult, TRequest>` Dispatch**
* **Option 2: Separate Isolated Azure Functions UI Host (`AzureFunctions.AdminUi`) Over Typed HTTP Proxies (`Http*UseCaseProxy`)**
* **Option 3: Client-Side SPA (Blazor WebAssembly / React) Calling Admin API Directly**

## Decision Outcome

Chosen option: **"Option 1: Consolidate SSR Admin Portal and Admin API Inside `EstateDataCollection.AzureFunctions.Admin` Using In-Process `IUseCase<TResult, TRequest>` Dispatch"** (superseding the earlier two-host `AdminUi` proxy layout):

1. **`EstateDataCollection.Admin.UI` (Razor Class Library):**
   - Houses pure zero-logic SSR Razor Pages, Components, Layouts, Presentation ViewModels, `RazorComponentRenderer` (backed by ASP.NET Core `HtmlRenderer`), and embedded GOV.UK Frontend / DfE branding assets served via `EmbeddedUiAssetProvider` (cached in-memory with `ETag`).
   - Zero Azure SDK, Blob Storage, or client-side SPA/WASM runtime dependencies.
2. **`EstateDataCollection.AzureFunctions.Admin` (Unified Isolated Functions Host with `"routePrefix": ""`):**
   - **Admin Portal Routes (`Routes/Admin/*` & `Assets`):** Exposes SSR HTML GET/POST routes under `/manage/*` and static assets under `/assets/*` (`StaticAssetEndpoint`), resolving `IUseCase<TResult, TRequest>` handlers directly via DI and sharing common page helpers in `FunctionPageBase`.
   - **Admin API & Event Triggers (`Routes/Api/*` & `Events/*`):** Exposes machine-to-machine JSON endpoints under explicit `api/...` route templates (`/api/schemas`, `/api/schemas/verify`, `/api/schemas/engines`, `/api/schemas/tests/run`, `/api/schemas/{schemaName}/versions/{version}`, `.../schedule`, `.../tests`) with `AuthorizationLevel.Function`, plus Event Grid `IngestDraftSchemaEvent` for automated `draft-schemas` ingestion.

### Consequences

* Good, because operations deploy and monitor two Function Apps (`Public` and `Admin`) instead of three, cutting cold-start, networking, and CI/CD overhead.
* Good, because Admin UI handlers invoke `IUseCase<TResult, TRequest>` directly without double-hop HTTP serialization or key forwarding, while keeping the exact same Application boundaries as the headless API.
* Good, because `Routes/Admin/*` and `Routes/Api/*` remain cleanly separated inside `EstateDataCollection.AzureFunctions.Admin` (and `Admin.UI` has zero Blob Storage dependency), making a future split back into a dedicated Admin UI Function App straightforward when Entra Easy Auth / network isolation boundaries warrant it.
* Bad, because hosting `/manage/*` and `/api/*` together requires route-scoped authentication middleware once Entra ID login for `/manage/*` is enabled (deferred architectural backlog item; see [Deployment Guide](../DEPLOYMENT.md#portal-authentication--network-isolation)).

### Confirmation

Compliance is confirmed via:
- Unit tests in [`RazorComponentRenderingTests.cs`](../../test/EstateDataCollection.Tests.Unit/AdminUi/RazorComponentRenderingTests.cs) verifying SSR HTML output, `SchemaFieldTree` rendering, and embedded asset caching (`EmbeddedUiAssetProvider`).
- End-to-end integration tests in [`AdminUiPagesIntegrationTests.cs`](../../test/EstateDataCollection.Tests.Integration/AdminUi/AdminUiPagesIntegrationTests.cs) and [`AdminUiStatisticsAndSubmissionInspectorIntegrationTests.cs`](../../test/EstateDataCollection.Tests.Integration/AdminUi/AdminUiStatisticsAndSubmissionInspectorIntegrationTests.cs) exercising `/manage/*` page handlers against Azurite.

## More Information

Related ADRs:
* [ADR 0001: Clean Architecture & Generic Use Case (`IUseCase`) Pattern](0001-clean-architecture-and-use-cases.md)
* [ADR 0006: Flex Consumption Self-Contained Deployment](0006-flex-consumption-self-contained-deployment.md)
* [ADR 0018: Zero-Logic Razor Views & Presentation ViewModel Pattern](0018-zero-logic-razor-views-and-presentation-viewmodels.md)
