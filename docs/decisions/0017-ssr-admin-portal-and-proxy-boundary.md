---
status: "accepted"
sub-status: "implemented"
date: 2026-11-17
decision-makers: ["Chris Kelly"]
---

# 17. Server-Side Rendered Admin Portal and Proxy-Only API Boundary

## Context and Problem Statement

The Estate Data Collection (EDC) platform required an internal administration portal for Education Estates Directorate operators to inspect schema contracts, preview extracted fields and business rules, dry-run verify ODCS YAML and JSON Schema contracts, manage lifecycle windows, and execute stored regression tests.

Historically, Admin operations were exposed solely as headless HTTP endpoints on [`EstateDataCollection.AzureFunctions.Admin`](../../src/EstateDataCollection.AzureFunctions.Admin) secured by function keys (`x-functions-key`). Embedding UI HTML rendering or direct Blob Storage dependencies inside the core Admin API would blur API/UI boundaries, while shipping a client-side SPA (React/Angular/Blazor WASM) would introduce separate Node build toolchains, CORS surface area, and client-side credential exposure. How should the Admin UI be architected and hosted within the existing Clean Architecture and isolated Azure Functions solution?

## Decision Drivers

* **Strict Trust Boundary & Single Source of Truth:** The UI host must never read or mutate Blob Storage directly; all contract mutations, verification, lifecycle changes, and test runs must pass through the audited Admin API use cases.
* **No Separate Frontend Build Toolchain:** Maintain a pure .NET 10 / C# build and deployment pipeline compatible with [ADR 0006](0006-flex-consumption-self-contained-deployment.md) and [ADR 0009](0009-central-package-management-and-build-props.md).
* **DfE & GOV.UK Design System Compliance:** Render compliant GOV.UK Frontend HTML markup with embedded CSS, fonts, crest/favicon assets, and minimal progressive enhancement JS (`workbench.js`) without external CDN dependencies.
* **Host Isolation:** Keep UI traffic, static asset serving, and Entra/portal concerns separate from machine-to-machine Public ingestion and core Admin API functions.

## Considered Options

* **Option 1: Separate Isolated Azure Functions UI Host (`AzureFunctions.AdminUi`) Rendering Pure SSR Razor Components (`Admin.UI`) Over Typed HTTP Proxies (`Http*UseCaseProxy`)**
* **Option 2: Client-Side SPA (Blazor WebAssembly / React) Calling Admin API Directly**
* **Option 3: Embedding Razor Views Inside `EstateDataCollection.AzureFunctions.Admin`**

## Decision Outcome

Chosen option: **"Option 1: Separate Isolated Azure Functions UI Host Rendering Pure SSR Razor Components Over Typed HTTP Proxies"**, structured across two projects:

1. **`EstateDataCollection.Admin.UI` (Razor Class Library):**
   - Houses SSR Razor Pages, Components, Layouts, ViewModels, `RazorComponentRenderer` (backed by ASP.NET Core `HtmlRenderer`), and embedded GOV.UK Frontend / DfE branding assets served via `EmbeddedUiAssetProvider`.
   - Zero Azure SDK, Blob Storage, or client-side SPA/WASM runtime dependencies.
2. **`EstateDataCollection.AzureFunctions.AdminUi` (Isolated Functions Host):**
   - Exposes anonymous/portal-authenticated HTTP GET/POST routes under `/manage/*` and `/assets/*` (`StaticAssetEndpoint`) inheriting common helpers from `FunctionPageBase`.
   - Implements the exact same Application `IUseCase<TResult, TRequest>` contracts via typed HTTP proxy classes (`HttpGetSchemasUseCaseProxy`, `HttpGetSchemaDetailUseCaseProxy`, `HttpVerifySchemaUseCaseProxy`, `HttpSubmitSchemaUseCaseProxy`, `HttpScheduleSchemaUseCaseProxy`, `HttpRunSchemaTestsUseCaseProxy`, `HttpUpdateSchemaTestsUseCaseProxy`) built on `AdminApiUseCaseProxyBase` and forwarding `x-functions-key` from `AdminUiOptions`.
   - References `Application` contracts and `Admin.UI`, with zero project reference to `Infrastructure`.

### Consequences

* Good, because the Admin UI shares the exact same `IUseCase<TResult, TRequest>` request/response contracts and `Result<T>` error semantics as the Admin API without duplicating domain or storage logic.
* Good, because GOV.UK Frontend pages render server-side with full progressive enhancement (`GET`/`POST`/PRG flows and optional `fetch` partial verification on `/manage/workbench/verify`) and zero browser-side secret exposure.
* Good, because UI endpoints, Admin API endpoints, and Public ingestion endpoints scale and deploy independently.
* Bad, because adding a new UI operation requires both an Admin API endpoint and a matching `Http*UseCaseProxy` adapter in `AzureFunctions.AdminUi`.

### Confirmation

Compliance is confirmed via:
- Unit tests in [`AdminApiUseCaseProxyTests.cs`](../../test/EstateDataCollection.Tests.Unit/AdminUi/AdminApiUseCaseProxyTests.cs) verifying proxy HTTP method, route, headers (`x-functions-key`), and `Result<T>` error mapping.
- Unit tests in [`RazorComponentRenderingTests.cs`](../../test/EstateDataCollection.Tests.Unit/AdminUi/RazorComponentRenderingTests.cs) verifying SSR HTML output and embedded asset resolution (`EmbeddedUiAssetProvider`).
- End-to-end integration tests in [`AdminUiPagesIntegrationTests.cs`](../../test/EstateDataCollection.Tests.Integration/AdminUi/AdminUiPagesIntegrationTests.cs) wiring UI routes to live Admin API handlers and Azurite.

## More Information

Related ADRs:
* [ADR 0001: Clean Architecture & Generic Use Case (`IUseCase`) Pattern](0001-clean-architecture-and-use-cases.md)
* [ADR 0006: Flex Consumption Self-Contained Deployment](0006-flex-consumption-self-contained-deployment.md)
* [ADR 0018: Zero-Logic Razor Views & Presentation ViewModel Pattern](0018-zero-logic-razor-views-and-presentation-viewmodels.md)
