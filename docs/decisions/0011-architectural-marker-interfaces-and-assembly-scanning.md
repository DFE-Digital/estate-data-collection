---
status: "proposed"
sub-status: "implemented"
date: 2026-10-21
decision-makers: ["Chris Kelly"]
---

# 11. Architectural Marker Interfaces and Assembly-Anchored Scrutor Scanning

## Context and Problem Statement

In Clean Architecture implementations, registering application services, repositories, validators, and instrumentation 
across layers often results in extensive manual dependency injection (`services.AddScoped<IFoo, Foo>()`) in application 
startup files. 

Manual DI leads to:
1. Forgotten registrations causing runtime `InvalidOperationException` errors on startup or during specific request branches.
2. Fragile assembly scanning that relies on arbitrary service types (e.g. `FromAssemblyOf<SomeService>()`), which break 
   if classes are renamed or refactored.
3. Cross-layer coupling if presentation projects need to know the internal concrete types of lower layers.

How should common marker abstractions, Scrutor scanning, and layer dependency injection be structured?

## Decision Drivers

* **Clean Separation of Abstractions:** Centralise shared architectural marker interfaces (`IDomain`, `IApplication`, 
  `IInfrastructure`, `IInstrumentation`, `IOption<T>`) in a zero-dependency `EstateDataCollection.Common` library.
* **Deterministic Assembly Anchoring:** Standardise assembly discovery across all layers (`AddDomain`, `AddApplication`, 
  `AddInfrastructure`) using explicit anchors (`typeof(DependencyInjection).Assembly`).
* **Automatic Registration:** Automatically register use cases, handlers, repositories, and instrumentation without manual 
  DI boilerplate.
* **Layer Encapsulation:** Presentation hosts must compose services via high-level extension methods without referencing 
  concrete internal implementations.

## Considered Options

* **Option 1: Zero-Dependency Marker Interfaces in `EstateDataCollection.Common` with Assembly-Anchored Scrutor Scanning**
* **Option 2: Embedding Marker Interfaces in the Domain Layer**
* **Option 3: Manual DI Registration for Every Service, Use Case, and Repository**

## Decision Outcome

Chosen option: **"Option 1: Zero-Dependency Marker Interfaces in `EstateDataCollection.Common` with Assembly-Anchored Scrutor 
Scanning"**, because it isolates architectural markers in a zero-dependency library while making service discovery automatic 
and refactoring-safe:

1. **Marker Abstractions in `EstateDataCollection.Common.Markers`**:
   Shared, dependency-free marker interfaces define the architectural role of each component:
   - `IDomain`: Marker for core domain services and domain logic.
   - `IApplication`: Marker for application-layer workflows and orchestration.
   - `IUseCase`: Marker for discrete use case handlers (registered with `IUseCase<TResult, TRequest>`).
   - `IInfrastructure`: Marker for external infrastructure adapters and storage repositories.
   - `IInstrumentation`: Marker for high-performance compile-time logging classes.
   - `IOption<TSelf>`: Marker for strongly typed configuration options.
2. **Explicit Assembly Anchoring**:
   Each architectural layer encapsulates its own DI registration in an extension method (`AddApplication`, `AddInfrastructure`), 
   scanning strictly its own assembly anchored by its own DI anchor type:
   ```csharp
   services.Scan(scan => scan
       .FromAssemblies(typeof(DependencyInjection).Assembly)
       .AddClasses(classes => classes.AssignableTo<IApplication>())
       .AsImplementedInterfaces()
       .WithScopedLifetime()
       .AddClasses(classes => classes.AssignableTo<IUseCase>())
       .AsImplementedInterfaces()
       .WithScopedLifetime()
       .AddClasses(classes => classes.AssignableTo<IInstrumentation>())
       .AsSelf()
       .WithTransientLifetime());
   ```
3. **Clean Composition in Presentation Host**:
   The presentation entry point (`Program.cs` in `EstateDataCollection.AzureFunctions`) acts as a clean composition root, 
   calling high-level extension methods without referencing internal concrete classes:
   ```csharp
   services
       .AddDomain()
       .AddApplication()
       .AddInfrastructure(builder.Configuration)
       .AddMonitoring(builder.Configuration);
   ```

### Consequences

* Good, because adding a new use case, repository, or validator requires only implementing its interface and marker; it 
  is registered automatically with the correct lifetime.
* Good, because assembly scanning is anchored to `typeof(DependencyInjection).Assembly` in each project, preventing broken 
  reflection paths when classes are renamed.
* Good, because presentation hosts have no compile-time knowledge of internal implementation classes in `Infrastructure` 
  or `Application`.
* Good, because architectural roles dictate DI lifetimes uniformly (e.g. use cases and repositories are Scoped; 
  instrumentation is Transient).
* Bad, because developers must ensure new classes inherit the appropriate marker interface for automated discovery.

### Confirmation

Compliance is confirmed via:
- Solution architecture dependency boundaries: `Common` has zero project references and zero third-party dependencies.
- Unit tests verifying service resolution and presentation controllers with mocked use cases.
- Bootstrapping tests confirming all `IUseCase` and `IInfrastructure` types resolve cleanly from the DI container.

## Pros and Cons of the Options

### Option 1: Dedicated Common Library & Assembly-Anchored Scrutor (Chosen)

* Good, because markers and abstractions are isolated in a zero-dependency library.
* Good, because presentation triggers remain strictly isolated from domain rules.
* Good, because Scrutor assembly scanning is deterministic and anchored on `typeof(DependencyInjection).Assembly`.
* Bad, because it introduces an extra assembly in the solution.

### Option 2: Embedding Marker Interfaces in Domain

* Good, because fewer project files are maintained.
* Bad, because application and infrastructure markers pollute pure domain boundaries.
* Bad, because domain models become entangled with service lifetimes.

### Option 3: Manual DI Registration for Every Service

* Good, because explicit registration requires no reflection or assembly scanning.
* Bad, because developers must remember to register every new repository and use case manually, causing runtime resolution 
  errors.
* Bad, because startup files become hundreds of lines of repetitive boilerplate.

## More Information

Project Abstractions:
* `src/EstateDataCollection.Common/Markers/IDomain.cs`
* `src/EstateDataCollection.Common/Markers/IApplication.cs`
* `src/EstateDataCollection.Common/Markers/IInfrastructure.cs`
* `src/EstateDataCollection.Common/Markers/IInstrumentation.cs`
* `src/EstateDataCollection.Common/Markers/IOption.cs`
