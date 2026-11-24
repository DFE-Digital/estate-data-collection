# OpenTelemetry Span Boundaries: Endpoint Wrappers vs. Use-Case Scopes

This spike examines where custom OpenTelemetry `Activity` spans and `Meter` metrics should begin and end in the **Estate Data Collection (EDC)** platform — specifically whether custom spans should live solely inside use-case scopes (`SubmissionExecutionScope`) or also wrap the shared HTTP endpoint and event trigger pipelines (`FunctionEndpointBase` and `FunctionEventBase`).

> [!NOTE]
> This investigation reflects the current observability setup in [`MonitoringExtensions.cs`](../../../src/EstateDataCollection.Infrastructure/Monitoring/MonitoringExtensions.cs), [`EstateDataCollectionTelemetry.cs`](../../../src/EstateDataCollection.Common/Telemetry/EstateDataCollectionTelemetry.cs), [`SubmissionExecutionScope.cs`](../../../src/EstateDataCollection.Application/UseCases/Submissions/Audit/SubmissionExecutionScope.cs), [`FunctionEndpointBase.cs`](../../../src/EstateDataCollection.Common.Web/Endpoints/FunctionEndpointBase.cs), and [`FunctionEventBase.cs`](../../../src/EstateDataCollection.Common.Web/Events/FunctionEventBase.cs).

---

## Current Observability Setup

`MonitoringExtensions.AddMonitoring` registers three trace sources and three meter groups:

- `options.ServiceName` (e.g. the host application service name)
- `EstateDataCollection.*` (wildcard catching all custom `ActivitySource` and `Meter` names prefixed with `EstateDataCollection.`)
- `Microsoft.Azure.Functions.Worker` (built-in Azure Functions isolated worker host instrumentation)
- `AddHttpClientInstrumentation()` (outbound HTTP spans/metrics)

On the application side:

1. **`SubmissionExecutionScope` (Application layer)** starts a parent `Activity` named `Submission:{Action}` on `EstateDataCollectionTelemetry.ApplicationActivitySource` (`"EstateDataCollection.Application"`) and a child `Activity` per catalogued step (`SchemaResolution`, `DraftPayloadMerger`, `JsonSchemaValidator`, `QualityRulesValidator`, `IdentifierExtraction`, `BlobStorage:*`), alongside metrics (`edc.submission.process.duration`, `edc.submission.process.executions`, `edc.submission.audit.events`) and `[LoggerMessage]` logs via `ProcessExecutionInstrumentation`.
2. **`FunctionEndpointBase` and `FunctionEventBase` (Web / Trigger layer)** emit structured `[LoggerMessage]` logs through `FunctionEndpointInstrumentation` and `FunctionEventInstrumentation`, but **do not** emit `Activity` spans or `Meter` measurements of their own.

---

## The Architectural Question

When an HTTP request or event arrives in Azure Functions, the worker host already creates an outer span via `Microsoft.Azure.Functions.Worker`. The design choice is:

- **Option A (Current): Use-case / process spans only.** Rely on `Microsoft.Azure.Functions.Worker` at the host boundary and only create custom `Activity` spans inside `SubmissionExecutionScope` (and any future domain scopes).
- **Option B: Endpoint / Event wrapper spans + Use-case spans.** Also start a lightweight span and timer/counter in `FunctionEndpointBase` (`Endpoint:{EndpointName}`) and `FunctionEventBase` (`Event:{EventName}`) around request binding, use-case dispatch, and HTTP/event outcome mapping.

---

## Trace Waterfall Comparison Across Scenarios

### 1. HTTP Submission Happy Path (`PUT /api/submissions/{id}`)

In **Option A**:

```text
[Microsoft.Azure.Functions.Worker] UpdateSubmissionFunction          (total request time)
  └── [Application] Submission:UpdateSubmission                      (use-case body only)
        ├── [Application] SchemaResolution
        ├── [Application] JsonSchemaValidator
        ├── [Application] QualityRulesValidator
        ├── [Application] IdentifierExtraction
        └── [Application] BlobStorage:SaveDraftPayload
```

In **Option B**:

```text
[Microsoft.Azure.Functions.Worker] UpdateSubmissionFunction          (total worker invocation)
  └── [Web] Endpoint:UpdateSubmission                                (tags: endpoint.name, http.status)
        └── [Application] Submission:UpdateSubmission                (use-case body)
              ├── [Application] SchemaResolution
              ├── [Application] JsonSchemaValidator
              ├── [Application] QualityRulesValidator
              ├── [Application] IdentifierExtraction
              └── [Application] BlobStorage:SaveDraftPayload
```

Here, Option B adds one extra parent span between the Azure Functions host span and `Submission:UpdateSubmission`. For submission endpoints alone, that single wrapper adds modest value when the request succeeds.

---

### 2. Request-Binding or JSON-Deserialisation Failure (`400 Bad Request`)

Consider a client sending malformed JSON or an empty body to an endpoint. `FunctionEndpointBase.ExecuteAsync` catches the failure inside `bindRequestAsync()` and returns early — the use case is **never invoked**.

In **Option A**:

```text
[Microsoft.Azure.Functions.Worker] UpdateSubmissionFunction          (generic worker span)
  └── (no custom span, no custom metric; only a Warning log via EndpointBindingFailed)
```

In **Option B**:

```text
[Microsoft.Azure.Functions.Worker] UpdateSubmissionFunction
  └── [Web] Endpoint:UpdateSubmission                                (tags: endpoint.name, outcome=binding_failed, error.code=JsonMalformed, http.status=400)
```

Option B surfaces binding/payload-parse failures in **distributed traces and RED metrics** (Rate, Errors, Duration) consistently, rather than leaving them as isolated log lines.

---

### 3. Non-Submission Endpoints (Schema Management & Admin UI)

Endpoints such as `CreateSchema`, `VerifySchema`, `ScheduleSchema`, `RunSchemaTests`, or Admin UI handlers do **not** use `SubmissionExecutionScope`.

In **Option A**:

```text
[Microsoft.Azure.Functions.Worker] VerifySchemaFunction
  └── (no custom application spans or metrics at all)
```

In **Option B**:

```text
[Microsoft.Azure.Functions.Worker] VerifySchemaFunction
  └── [Web] Endpoint:VerifySchema                                    (tags: endpoint.name, http.status, duration)
```

Without wrapper spans in `FunctionEndpointBase`, non-submission endpoints have zero custom OpenTelemetry metrics or outcome tags — you only see raw Azure Functions worker execution unless you also build dedicated scopes for Schema/Admin use cases.

---

### 4. Event-Triggered Functions (`FunctionEventBase`)

For blob- or queue-triggered ingestion handlers (`DraftBlobIngestedEvent` etc.):

In **Option A**:

```text
[Microsoft.Azure.Functions.Worker] DraftBlobIngestedFunction
  └── [Application] Submission:IngestDraftBlob                       (only if use case creates a scope; absent on bind failure)
```

In **Option B**:

```text
[Microsoft.Azure.Functions.Worker] DraftBlobIngestedFunction
  └── [Web] Event:DraftBlobIngested                                  (tags: event.name, trigger.source, outcome, error.code)
        └── [Application] Submission:IngestDraftBlob
```

Option B guarantees every trigger invocation records a uniform outcome tag and duration histogram even if blob payload binding fails before reaching the use case.

---

### 5. Direct Execution Outside the Functions Host (Integration & Unit Tests)

When tests invoke `useCase.ExecuteAsync(...)` directly (or when in-process harnesses bypass the Azure Functions middleware), `Microsoft.Azure.Functions.Worker` is not in the call stack.

- **`SubmissionExecutionScope`** must **continue** creating its own `Submission:{Action}` root span whenever no parent exists (otherwise `Activity.Current` would be `null` in unit/integration tests and background runners).
- **Note on `ActivitySource.StartActivity`**: in .NET `System.Diagnostics`, `StartActivity` automatically parents under `Activity.Current` if one is active, or starts a new root trace if `Activity.Current` is `null`. That means Option B and `SubmissionExecutionScope` compose naturally without special wiring.

---

## Trade-Offs Summary

| Dimension | Option A: `SubmissionExecutionScope` Only | Option B: `FunctionEndpointBase` / `FunctionEventBase` + Scope |
| --- | --- | --- |
| **Span count per request** | Minimal — no extra span between Worker host and `Submission:{Action}`. | +1 wrapper span (`Endpoint:{Name}` or `Event:{Name}`) per invocation. |
| **Telemetry cost / volume** | Lower span ingestion volume in Application Insights / OTLP. | Slightly higher span count (1 extra span per request), offset by unified metrics. |
| **Binding & parse failures** | Visible only in logs (`EndpointBindingFailed`), missing from trace status and error metrics. | Captured in both trace tags (`error.code`, `http.status`) and RED metrics. |
| **Schema & Admin UI coverage** | Uninstrumented by custom spans/metrics unless every domain area adds its own scope. | Immediate baseline latency, status code, and error metrics across **every** endpoint and event. |
| **Azure Functions host independence** | Coupled to `Microsoft.Azure.Functions.Worker` for HTTP status/latency outside submissions. | Consistent EDC tags (`endpoint.name`, `http.status`, `outcome`) regardless of host quirks or test harness. |

---

## Key Considerations If Adopting Option B

1. **Keep tag cardinality strictly bounded.** Never put `submissionId`, URLs with GUIDs, or arbitrary error descriptions as metric tags. Use `endpoint.name` / `event.name`, `http.status` (or status class `2xx`/`4xx`/`5xx`), and `outcome` (`succeeded` / `binding_failed` / `usecase_failed`) on metrics; keep high-cardinality IDs (`submission.id`, `error.code`) strictly on `Activity` span tags and structured logs.
2. **Reuse the `EstateDataCollection.*` convention.** Define `WebSourceName = "EstateDataCollection.Web"` / `WebMeterName = "EstateDataCollection.Web"` (or extend `EstateDataCollectionTelemetry`) so `MonitoringExtensions` picks them up automatically via `.AddSource("EstateDataCollection.*")` and `.AddMeter("EstateDataCollection.*")`.
3. **Preserve `SubmissionExecutionScope` autonomy.** Even with endpoint wrappers, `SubmissionExecutionScope` should continue starting `Submission:{Action}` spans so direct use-case calls, tests, and multi-step background jobs remain self-contained.
4. **Watch the `handlerAsync` overload in `FunctionEndpointBase`.** Notice `FunctionEndpointBase` has both `ExecuteAsync(useCase, bindRequestAsync, ...)` and `ExecuteAsync(handlerAsync, ...)` (used by Admin UI / custom handlers). Both should wrap the same span/metric helper so Admin UI routes get identical visibility.

---

## Suggested Next Steps

1. Keep **Option A** for the current iteration while `SubmissionExecutionScope` telemetry stabilises in Application Insights / OTLP.
2. When expanding observability to **Schema Management** and **Admin UI** endpoints, implement **Option B** in `FunctionEndpointBase` and `FunctionEventBase` using a `WebActivitySource` (`"EstateDataCollection.Web"`) and `edc.endpoint.*` / `edc.event.*` metrics.
3. Verify in a local OTLP collector (e.g. Aspire Dashboard or Jaeger) that `Microsoft.Azure.Functions.Worker` -> `Endpoint:{Name}` -> `Submission:{Action}` -> step spans form a clean, non-redundant hierarchy before exporting to production Application Insights.
