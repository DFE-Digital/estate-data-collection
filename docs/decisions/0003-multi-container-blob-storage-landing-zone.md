---
status: "proposed"
date: 2026-10-21
decision-makers: ["Chris Kelly"]
---

# 3. Multi-Container Blob Storage Landing Zone (schemas, payloads, processed)

## Context and Problem Statement

The EDC platform requires an immutable, resilient, and cost-effective storage architecture for condition survey data. 
Survey data progresses through clear stages: authoritative contract schemas, in-flight draft survey submissions, and 
finalised, validated survey envelopes ready for downstream CDE ingestion.

Storing all stages within a single storage container or folder hierarchy creates access control ambiguity, complicates 
lifecycle retention rules, and blurs boundaries between unvalidated drafts and verified canonical records. How should 
storage containers and data segregation be structured in Azure Blob Storage?

## Decision Drivers

* **Security & Access Boundary Segregation:** Different identity personas and downstream services require distinct 
  access permissions (e.g. data teams write schemas, surveyors write drafts, downstream CDE readers consume finalised 
  envelopes).
* **Auditability & Immutability:** Finalised surveys must be stored as immutable, verified historical snapshots with 
  SHA-256 integrity hashes.
* **Lifecycle Governance:** Ability to attach Azure Blob lifecycle management policies (e.g. soft-delete, tiering, 
  90-day draft cleanup) independently to each stage.
* **Zero-Trust Managed Identity:** Authentication must rely strictly on Microsoft Entra ID without storage account 
  master keys.

## Considered Options

* **Option 1: Multi-Container Segregation (`schemas`, `payloads`, `processed`, `landing`, `cleared`, `quarantine`)**
* **Option 2: Single Monolithic Container with Subfolder Partitioning**
* **Option 3: Separate Storage Accounts per Lifecycle Stage**

## Decision Outcome

Chosen option: **"Option 1: Multi-Container Segregation"**, because it provides strong, native RBAC boundaries, distinct 
lifecycle management policies, and clean operational segregation without the overhead of multiple storage accounts:

1. **`schemas` Container (Contract Registry)**:
   Stores validated JSON Schema Draft 2020-12 contracts and version metadata:
   - Path: `{schemaName}/v{version}.json` and `{schemaName}/metadata.json`
   - Read by the ingestion engine during payload validation; written by data governance teams.
2. **`payloads` Container (Draft Staging Zone)**:
   Stores mutable in-flight surveyor draft sessions:
   - Path: `{submissionId}.json`
   - Governed by short-term retention (e.g. 30-day draft purge policy).
3. **`processed` Container (Finalised Canonical Archive)**:
   Stores validated, immutable submission envelopes promoted upon successful finalisation:
   - Path: `{urn}/{year}/{submissionId}.json`
   - Tagged with SHA-256 integrity hash, URN, schema version, and timestamp metadata for downstream Common Data 
     Environment (CDE) ingestion.
4. **Binary Upload Tiers (`landing`, `cleared`, `quarantine`)**:
   Dedicated containers for surveyor file attachments undergoing Microsoft Defender for Storage malware scanning.

### Consequences

* Good, because Azure RBAC roles (`Storage Blob Data Contributor`, `Storage Blob Data Reader`) can be assigned per 
  container.
* Good, because Azure Blob Lifecycle Management rules can purge stale drafts in `payloads` without affecting finalised 
  records in `processed`.
* Good, because deterministic container paths enable direct, low-latency blob point lookups without full-container 
  index scans.
* Bad, because promoting a draft to finalised requires a blob copy operation from `payloads` to `processed`.

### Confirmation

Compliance is confirmed via:
- Infrastructure automated initialisation in `AzureBlobStorageService` verifying container existence on boot.
- Integration tests executing real blob copy, promotion, and metadata tagging against the Azurite emulator.
- Access reviews ensuring no master account keys are configured.

## Pros and Cons of the Options

### Option 1: Multi-Container Segregation (Chosen)

* Good, because it provides native security boundaries and independent lifecycle management.
* Good, because promotion between stages is explicit and auditable.
* Good, because container names are configurable via strongly typed `BlobStorageOptions`.
* Bad, because cross-container copy operations must be managed during draft finalisation.

### Option 2: Single Monolithic Container with Subfolders

* Good, because only one container needs to be created.
* Bad, because Azure RBAC permissions cannot be scoped cleanly to virtual folder prefixes.
* Bad, because lifecycle management rules apply to the entire container unless complex prefix filters are maintained.

### Option 3: Separate Storage Accounts per Stage

* Good, because it provides absolute resource and billing isolation.
* Bad, because provisioning, monitoring, and managing multiple storage accounts introduces unnecessary operational 
  overhead and cost for an MVP.

## More Information

Implementation:
* `src/EstateDataCollection.Infrastructure/Storage/AzureBlobStorageService.cs`
* `src/EstateDataCollection.Infrastructure/Configuration/BlobStorageOptions.cs`
