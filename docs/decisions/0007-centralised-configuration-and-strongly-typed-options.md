---
status: "accepted"
date: 2026-10-21
decision-makers: ["Chris Kelly"]
---

# 7. Centralised Configuration with Strongly-Typed Options and Azure App Configuration

## Context and Problem Statement

Cloud-native enterprise services require robust configuration management supporting dynamic updates, strong typing, 
startup fail-fast validation, and zero-trust security without storing secrets or unvalidated environment strings in code.

In the Estate Data Collection (EDC) service, components across the Domain, Application, and Infrastructure layers require 
configuration settings—such as Azure Blob Storage account names, container mappings (`schemas`, `payloads`, `processed`), 
SAS token lifetimes, and maximum payload size limits. 

Hardcoding `Environment.GetEnvironmentVariable("...")` lookups leads to silent runtime failures from typos, impedes 
testability, and scatters configuration keys across the codebase. How should configuration be structured to guarantee 
type safety, fail-fast startup validation, and zero-trust security?

## Decision Drivers

* **Centralised Governance:** Single source of truth for application configuration across Azure environments.
* **Strong Typing & Fail-Fast Validation:** Strongly typed C# options records validated at application startup using 
  data annotations (`ValidateDataAnnotations`, `ValidateOnStart`).
* **Zero-Trust Security:** Zero credentials or secrets stored in code or configuration; secrets resolved via Key Vault 
  references using Microsoft Entra ID.
* **Local Development Ergonomics:** Seamless fallback to `local.settings.json` or in-memory settings during local testing 
  without cloud dependencies.

## Considered Options

* **Option 1: Azure App Configuration with Assembly-Scanned `IOption<T>` Options Pattern**
* **Option 2: Direct Environment Variable Lookups (`Environment.GetEnvironmentVariable`)**
* **Option 3: Static Configuration Helper Classes**

## Decision Outcome

Chosen option: **"Option 1: Azure App Configuration with Assembly-Scanned `IOption<T>` Options Pattern"**, because it 
provides strongly typed configuration, dynamic reload capabilities, and fail-fast startup validation:

1. **Self-Describing Options via Marker (`IOption<TSelf>`)**:
   Options classes implement the marker interface with C# static abstract members:
   ```csharp
   public interface IOption<TSelf> where TSelf : class, IOption<TSelf>
   {
       static abstract string SectionName { get; }
   }
   ```
2. **Automatic Assembly Scanning & Startup Validation**:
   `ConfigOptionsExtensions.AddConfigOptions(assemblies)` scans the specified assemblies for all `IOption<T>` 
   implementations using Scrutor as a type finder and automatically binds and validates them at application boot:
   ```csharp
   services.AddOptions<T>()
       .BindConfiguration(T.SectionName)
       .ValidateDataAnnotations()
       .ValidateOnStart();
   ```
   Missing or invalid configuration values fail immediately upon application boot rather than failing mid-flight during 
   request processing.
3. **Azure App Configuration Integration**:
   When deployed to Azure, `AzureAppConfigurationExtensions.AddAppConfiguration` connects via Microsoft Entra ID Managed 
   Identity (`DefaultAzureCredential`), supporting:
   - Centralised key-value management across environments.
   - Dynamic 5-minute sentinel key cache refresh (`refreshAll: true`) without requiring container restarts.
   - Key Vault secret reference resolution directly within App Configuration.
4. **Local Fallback**:
   In local development and test environments, options automatically bind to `local.settings.json` or in-memory configuration 
   dictionaries without cloud dependencies.

### Consequences

* Good, because configuration errors (e.g. negative SAS duration, missing storage endpoints) crash on boot via 
  `OptionsValidationException` before accepting traffic.
* Good, because non-breaking settings can be adjusted in Azure App Configuration and refresh within 5 minutes without 
  restarting Functions.
* Good, because secrets are referenced via Key Vault links and resolved using Managed Identity.
* Good, because unit and integration tests can inject `IOptions<TOptions>` or instantiate raw option POCOs without 
  setting system environment variables.
* Bad, because it requires provisioning an Azure App Configuration store in deployed cloud environments.

### Confirmation

Compliance is confirmed via:
- Startup tests checking that missing required configuration triggers immediate `OptionsValidationException`.
- Data annotation tests validating required attributes and valid ranges in `EstateDataCollection.Tests.Unit`.
- Integration tests providing test-specific option instances without environment variables.

## Pros and Cons of the Options

### Option 1: Azure App Configuration with Options Pattern (Chosen)

* Good, because it cleanly separates configuration structure from storage backends.
* Good, because `ValidateOnStart()` catches misconfigurations before requests are accepted.
* Good, because dynamic refresh enables zero-downtime setting changes.
* Bad, because it introduces an Azure SDK dependency in the infrastructure layer.

### Option 2: Direct Environment Variable Lookups

* Good, because it is simple to implement initially.
* Bad, because typos in variable names fail silently at runtime.
* Bad, because no startup validation or type safety is provided.

### Option 3: Static Configuration Helper Classes

* Good, because configuration keys are centralised in one file.
* Bad, because static classes hinder unit testing and prevent injecting mock settings.

## More Information

Implementation:
* `src/EstateDataCollection.Common/Markers/IOption.cs`
* `src/EstateDataCollection.Infrastructure/Configuration/ConfigOptionsExtensions.cs`
* `src/EstateDataCollection.Infrastructure/Configuration/AzureAppConfigurationExtensions.cs`
* `src/EstateDataCollection.Infrastructure/Configuration/BlobStorageOptions.cs`
