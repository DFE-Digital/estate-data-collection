---
status: "proposed"
date: 2026-10-21
decision-makers: ["Chris Kelly"]
---

# 5. Source-Generated Compile-Time Instrumentation and OpenTelemetry

## Context and Problem Statement

High-throughput ingestion services running on serverless compute require structured logging, distributed tracing, and 
metrics for operational monitoring. However, traditional runtime logging (`logger.LogInformation("Processing submission 
{Id} for schema {Name}", id, name)`) introduces boxing allocations, runtime string parsing, and lack of compile-time 
verification of event IDs or parameters.

Furthermore, monitoring must adhere to vendor-neutral OpenTelemetry standards while supporting Microsoft Azure Application 
Insights without tight vendor lock-in.

How should logging, distributed tracing, and metrics be implemented across the EDC Service to achieve zero-allocation 
performance and standard telemetry export?

## Decision Drivers

* **Zero-Allocation Logging:** Eliminate runtime string interpolation and argument array boxing on hot request paths.
* **Compile-Time Safety:** Event IDs, log levels, and template parameters must be verified at compile time by Roslyn 
  source generators.
* **Separation of Concerns:** Business use cases must not directly invoke `ILogger` primitives; telemetry concerns 
  should be encapsulated behind typed instrumentation interfaces.
* **OpenTelemetry Standardisation:** Native integration with OpenTelemetry activities, meters, and Azure Monitor exporters.

## Considered Options

* **Option 1: Compile-Time Source-Generated `[LoggerMessage]` with Typed `IInstrumentation` Classes and OpenTelemetry**
* **Option 2: Direct `ILogger<T>` Invocations in Use Cases**
* **Option 3: Aspect-Oriented Interception / Middleware Logging Only**

## Decision Outcome

Chosen option: **"Option 1: Compile-Time Source-Generated `[LoggerMessage]` with Typed `IInstrumentation` Classes and 
OpenTelemetry"**, because it provides zero runtime allocation, strongly typed parameters, and clear architectural 
separation:

1. **Typed Instrumentation per Feature**:
   Each operational use case has a dedicated partial instrumentation class implementing the marker `IInstrumentation`:
   ```csharp
   public partial class CreateSubmissionUseCaseInstrumentation : IInstrumentation
   {
       private readonly ILogger<CreateSubmissionUseCaseInstrumentation> _logger;

       [LoggerMessage(EventId = 2001, Level = LogLevel.Information, 
           Message = "Creating submission session for schema '{SchemaName}', requested version: {RequestedVersion}")]
       public partial void SessionCreating(string schemaName, int? requestedVersion);
   }
   ```
2. **Roslyn Compile-Time Code Generation**:
   The `[LoggerMessage]` attribute instructs the C# Roslyn compiler to generate optimised logging delegates at compile 
   time, eliminating string formatting and boxing at runtime.
3. **OpenTelemetry & Azure Monitor**:
   Telemetry is configured via `EstateDataCollection.AzureFunctions/Program.cs` using standard OpenTelemetry SDKs 
   (`AddOpenTelemetry()`), capturing incoming HTTP activities, outgoing Azure SDK calls, and exporting directly to Azure 
   Application Insights (`Azure.Monitor.OpenTelemetry.Exporter`).

### Consequences

* Good, because compile-time generation eliminates CPU overhead and GC allocations on high-frequency ingestion paths.
* Good, because log event IDs and message templates are statically verified during build.
* Good, because use cases depend only on clear semantic methods (`instrumentation.SessionCreated(...)`) rather than raw 
  `ILogger` calls.
* Good, because distributed tracing and metrics conform to vendor-neutral OpenTelemetry standards.
* Bad, because each use case requires a companion partial instrumentation class.

### Confirmation

Compliance is confirmed via:
- Build output verification confirming Roslyn source generators execute without warnings.
- Unit tests verifying instrumentation invocations using NSubstitute test doubles.
- End-to-end tracing verified via Application Insights OpenTelemetry exporters.

## Pros and Cons of the Options

### Option 1: Source-Generated Instrumentation & OpenTelemetry (Chosen)

* Good, because it delivers maximum performance and zero memory allocation.
* Good, because event IDs and parameter types are verified at compile time.
* Good, because it cleanly isolates observability from domain logic.
* Bad, because it requires defining partial instrumentation classes.

### Option 2: Direct ILogger Invocations in Use Cases

* Good, because it requires fewer files.
* Bad, because string interpolation and boxing degrade performance on high-throughput paths.
* Bad, because use case code becomes cluttered with logging statements.

### Option 3: Middleware Logging Only

* Good, because it requires zero logging code inside use cases.
* Bad, because detailed business context (e.g. schema versions, URNs, validation error counts) is lost.

## More Information

Implementation:
* `src/EstateDataCollection.Common/Markers/IInstrumentation.cs`
* `src/EstateDataCollection.Application/UseCases/*/*Instrumentation.cs`
* `src/EstateDataCollection.AzureFunctions/Program.cs`
