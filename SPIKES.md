# Engineering Spikes

This index tracks technical spikes, exploratory investigations, and research notes for the **Estate Data Collection (EDC)** platform. Each spike lives under [`docs/spikes/`](docs/spikes) grouped by theme.

> Most of these spikes where investigated from a seperate DfE team, apart of the _School Account Team_ was where these where researched at. These authors include, Chris Kelly & Andy Cunningham. 

---

## Index of Spikes

### OpenAPI Documentation

| Document | Summary |
| --- | --- |
| [**OpenAPI Documentation**](docs/spikes/openapi/openapi-documentation.md) | Review of .NET 10 OpenAPI tooling (`Microsoft.AspNetCore.OpenApi`, Scalar, Redoc, Swagger UI, NSwag, DocFX, Docusaurus) and 2026 recommendations. |
| [**Recommended Generation & Publishing of API Docs**](docs/spikes/openapi/recommended-generation-and-publishing-of-api-docs.md) | Build-time OpenAPI spec generation, static site rendering with Scalar, and CI/CD publishing via GitHub Actions. |

### Observability & Telemetry

| Document | Summary |
| --- | --- |
| [**OpenTelemetry Span Boundaries: Endpoint Wrappers vs. Use-Case Scopes**](docs/spikes/observability/endpoint-vs-usecase-opentelemetry-spans.md) | Architectural comparison of `SubmissionExecutionScope`-only tracing versus wrapping `FunctionEndpointBase` and `FunctionEventBase` with endpoint/event spans and RED metrics. |

### Resilience & Chaos Engineering

| Document | Summary |
| --- | --- |
| [**Introduction to resilience with Polly**](docs/spikes/resilience/introduction-to-resilience-with-polly.md) | Practical introduction to `AddStandardResilienceHandler`, `ConfigureHttpClientDefaults`, Polly defaults, per-dependency tuning, and running the playground. |
| [**Rawness of resiliency: retries, backoff and circuit breakers**](docs/spikes/resilience/rawness-of-resiliency-retries-backoff-and-circuit-breakers.md) | Deep dive into the resilience playground, comparing platform defaults to service-aware pipelines, observability, and next steps. |
| [**Implementing chaos strategies into a resilience pipeline**](docs/spikes/resilience/implementing-chaos-strategies-into-a-resilience-pipeline.md) | Layering Simmy chaos strategies (`Fault`, `Outcome`, `Latency`, `Behavior`) into Polly pipelines, risks, mitigations, and next steps. |

### API Versioning

| Document | Summary |
| --- | --- |
| [**Introduction to the principles of Versioning**](docs/spikes/versioning/introduction-to-the-principles-of-versioning.md) | Foundational principles: Hyrum's Law, API lifecycle states, additive vs. breaking changes, SemVer vs. CalVer, and backwards compatibility. |
| [**Versioning within .Net**](docs/spikes/versioning/versioning-within-dotnet.md) | Implementing versioning in ASP.NET Core with `Asp.Versioning` — version readers (query string, URL segment, header, media type) and conventions (attribute, centralised, namespace, custom). |
