---
status: "proposed"
sub-status: "implemented"
date: 2026-10-21
decision-makers: ["Chris Kelly"]
---

# 1. Clean Architecture and Generic Use Case (IUseCase) Pattern

## Context and Problem Statement

The Department for Education (DfE) Education Estates Directorate (EED) requires a machine-to-machine (M2M) ingestion 
service to collect, validate, and process school condition surveys. Initial hosting is targeted on Azure Functions 
(.NET 10 Isolated Worker), but long-term hosting strategy requires the flexibility to migrate or containerise into
Azure Container Apps (ACA), Kubernetes, or ASP.NET Core Minimal APIs without re-architecting domain logic or data 
ingestion pipelines.

Tightly coupling business logic to Azure Functions trigger bindings (such as `HttpRequestData`, `FunctionContext`, 
or function-specific attributes) creates vendor lock-in, complicates automated testing, and prevents hosting portability. 
How should the service be structured to ensure business workflows remain host-agnostic and testable?

## Decision Drivers

* **Hosting Portability:** Ability to migrate seamlessly between Azure Functions (.NET 10 Isolated Worker) and 
  Azure Container Apps (ACA) / ASP.NET Core Minimal APIs with zero changes to domain or application layers.
* **Separation of Concerns:** Business workflows, schema validation, and storage coordination must remain strictly 
  isolated from HTTP transport and serverless bindings.
* **Testability:** Complete unit testability of workflows in memory without mocking Azure Functions runtime plumbing or 
  HTTP trigger contexts.
* **Consistency:** Uniform error handling and railway-oriented result flows across all operational endpoints.

## Considered Options

* **Option 1: Clean Architecture with Generic `IUseCase<TResult, TRequest>` Orchestration**
* **Option 2: Framework-Coupled Serverless Triggers (Fat Function Triggers)**
* **Option 3: Vertical Slice Architecture in a Single Web API Project**

## Decision Outcome

Chosen option: **"Option 1: Clean Architecture with Generic `IUseCase<TResult, TRequest>` Orchestration"**, because it 
provides strict dependency inversion (`Domain` $\leftarrow$ `Application` $\leftarrow$ `Infrastructure` / 
`AzureFunctions`). The core domain logic and application workflows have zero dependencies on Azure Functions SDKs or 
HTTP bindings:

```csharp
public interface IUseCase<TResult, in TRequest>
{
    Task<Result<TResult>> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default);
}
```

1. **Domain Layer (`EstateDataCollection.Domain`)**: Completely free of external dependencies, hosting frameworks, or 
   persistence packages. Contains core domain entities (`SchemaMetadata`, `SubmissionEnvelope`, `Urn`).
2. **Application Layer (`EstateDataCollection.Application`)**: Encapsulates all workflow orchestration into discrete 
   single-responsibility use cases (`VerifySchemaUseCase`, `SubmitSchemaUseCase`, `CreateSubmissionUseCase`, 
   `UpdateSubmissionUseCase`, `FinaliseSubmissionUseCase`).
3. **Infrastructure Layer (`EstateDataCollection.Infrastructure`)**: Implements external concerns (Azure Blob Storage, 
   schema validation engine, telemetry, App Configuration).
4. **Presentation Layer (`EstateDataCollection.AzureFunctions`)**: Purely acts as a transport adapter. Azure Functions 
   HTTP triggers deserialize requests, invoke `useCase.ExecuteAsync(request)`, and transform domain `Result<T>` into 
   RFC 7807 `IActionResult` responses.

### Consequences

* Good, because presentation adapters can be swapped from Azure Functions to ASP.NET Core Minimal APIs or worker daemons 
  without altering application or domain code.
* Good, because all use cases can be unit tested in memory in milliseconds using plain C# objects and mocked interfaces.
* Good, because all endpoints return standard RFC 7807 problem details mapped consistently from railway-oriented 
  `Result<T>` and `Error` types.
* Bad, because it introduces additional DTO records (`*Request.cs`, `*Result.cs`) and abstraction layers between 
  transport triggers and persistence.

### Confirmation

Compliance is confirmed via:
- Solution architecture dependency rules: `Domain` has zero package references; `Application` references only `Domain` 
  and `Common`; `Infrastructure` and `AzureFunctions` implement contracts defined in `Application`.
- Unit tests in `EstateDataCollection.Tests.Unit.UseCases` validating each use case in isolation with NSubstitute test 
  doubles.
- Code review ensuring no trigger or HTTP attributes exist inside `Application` or `Domain`.

## Pros and Cons of the Options

### Option 1: Clean Architecture with Generic Use Cases (Chosen)

* Good, because it isolates serverless vendor dependencies to the outer presentation rim.
* Good, because single-responsibility use cases provide high cohesion and clear code navigation.
* Good, because railway-oriented results (`Result<T>`) eliminate unhandled exceptions for expected business failures.
* Bad, because it requires maintaining explicit request/result contracts and mapping layers.

### Option 2: Framework-Coupled Serverless Triggers (Fat Function Triggers)

* Good, because fewer files and interfaces are needed initially.
* Bad, because business logic becomes intertwined with `HttpRequestData`, query parameters, and trigger headers.
* Bad, because migrating to containerised runtimes (ACA/K8s) requires a complete rewrite of endpoint logic.
* Bad, because unit tests require complex mocking of Azure Functions worker execution contexts.

### Option 3: Vertical Slice Architecture in a Single Web API Project

* Good, because feature folders keep handlers and models closely co-located.
* Bad, because boundary enforcement between domain rules, storage access, and transport is difficult to maintain in multi-developer teams without project separation.
* Bad, because extracting sub-services or transitioning hosts requires untangling mixed dependencies.

## More Information

Project Structure:
* `src/EstateDataCollection.Domain`: Core entities, value objects (`Urn`), and domain results.
* `src/EstateDataCollection.Application`: `IUseCase<TResult, TRequest>` contracts and workflow handlers.
* `src/EstateDataCollection.Infrastructure`: Persistence, blob storage repositories, and configuration.
* `src/EstateDataCollection.AzureFunctions`: Presentation HTTP triggers and entry point.
