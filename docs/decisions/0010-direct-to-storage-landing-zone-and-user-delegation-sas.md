---
status: "accepted"
date: 2026-10-21
decision-makers: ["Chris Kelly"]
---

# 10. Direct-to-Storage Landing Zone with User Delegation SAS and Defender Quarantine

## Context and Problem Statement

Surveying organisations inspecting school premises frequently capture binary attachments (e.g., high-resolution condition 
photographs, structural CAD/floor plans, asbestos registers, and Energy Performance Certificates [EPCs]) ranging from tens 
to hundreds of megabytes per file.

Routing large binary uploads directly through API gateways or HTTP application workers (such as Azure Functions or Azure 
Container Apps) introduces significant operational risks:
1. Compute memory saturation and worker thread exhaustion caused by buffering large streams.
2. HTTP timeout limits over slow, intermittent cellular or surveyor site network connections.
3. In-process antivirus inspection overhead consuming application CPU cycles.

Furthermore, long-lived storage account master keys (`AccountKey`) stored in configuration or key vaults violate DfE 
zero-trust security standards.

How should EDC securely ingest, scan, triage file attachments, and organise storage containers without routing raw file 
bytes through the application host?

## Decision Drivers

* **Zero-Trust Security & Identity:** No long-lived Storage Account keys (`AccountKey`) stored in configuration or key 
  vaults; all access must use Microsoft Entra ID.
* **Least Privilege Access:** File upload access tokens must be narrowly scoped to a single blob path, write-only, and 
  short-lived (15-minute expiry).
* **Malware Quarantine Automation:** Automatic asynchronous malware detection via Microsoft Defender for Storage before 
  any internal system or downstream consumer can access the file.
* **Worker Scalability:** Compute workers only handle lightweight JSON control-plane operations; binary data plane streams 
  directly between the client and Azure Blob Storage.
* **Storage Segregation:** Clear container boundaries separating contracts (`schemas`), staged submissions (`payloads`), 
  evaluated records (`processed`), and binary upload tiers (`landing`, `cleared`, `quarantine`).

## Considered Options

* **Option 1: Direct Client Upload via User Delegation SAS with Defender Event-Driven Quarantine**
* **Option 2: Multipart Form-Data Streaming Through EDC HTTP API Endpoints**
* **Option 3: Storage Account Master Shared Access Signatures (Account Keys / Key Vault)**

## Decision Outcome

Chosen option: **"Option 1: Direct Client Upload via User Delegation SAS with Defender Event-Driven Quarantine"**, because 
it eliminates memory and compute pressure on the application host, removes storage master keys from the architecture, 
and isolates unverified files automatically:

1. **User Delegation Keys via Microsoft Entra ID**:
   Instead of generating SAS tokens with master storage account keys, the service requests a short-lived User Delegation 
   Key (`GetUserDelegationKeyAsync`) using its own Entra ID Managed Identity (`TokenCredential`). The resulting SAS URI is 
   cryptographically signed using OAuth 2.0 credentials.
2. **Narrow Scope & Short Expiry**:
   Clients request an upload URL from EDC. The service returns a presigned URL scoped strictly to:
   - Specific blob path: `landing/{orgId}/{submissionId}/{uniqueBlobName}`
   - Permission: `Create | Write` only (no read, delete, or list permissions)
   - Expiration: Short-lived (15 minutes)
3. **Decoupled Malware Scanning (Microsoft Defender for Storage)**:
   Uploaded blobs stream directly from the surveyor client to the `landing` container. Microsoft Defender for Storage 
   automatically scans the uploaded blob asynchronously.
4. **Event-Driven Isolation**:
   Upon scan completion, an Azure Event Grid notification triggers the triage handler:
   - Clean files are moved to the `cleared` container for downstream processing.
   - Infected or suspicious files are immediately moved to the `quarantine` container and blocked from processing.
5. **Container Segregation**:
   Azure Blob Storage maintains clear container boundaries:
   - `schemas`: Governed JSON schemas and conformance suites.
   - `payloads`: Staged draft survey JSON payloads.
   - `processed`: Finalised, validated survey envelopes.
   - `landing`: Unscanned surveyor file attachments awaiting Defender triage.
   - `cleared`: Antivirus-verified clean file attachments.
   - `quarantine`: Malicious or unverified files safely segregated.

### Consequences

* Good, because application workers remain ultra-lightweight and stateless with near-zero memory footprint during 100MB+ 
  file uploads.
* Good, because it completely eliminates storage account master keys from configuration and code.
* Good, because compromised or malicious files are isolated in `quarantine` and never exposed to downstream processing 
  pipelines.
* Good, because clean files in `cleared` are safely referenced by finalised survey envelopes.
* Bad, because clients must execute a two-step flow: request file upload metadata from EDC, then `PUT` the binary stream 
  directly to Azure Storage.

### Confirmation

Compliance is confirmed via:
- Unit tests verifying `IBlobStorageService` enforces file name sanitisation regex (`^[A-Za-z0-9._-]{1,120}$`), calculates 
  correct expiry windows, and generates scoped SAS URLs.
- Event Grid handler tests validating that Defender scan verdicts correctly transition blobs to `cleared` or `quarantine`.
- Integration tests verifying all storage containers are initialised and accessible via `AzureBlobStorageService`.

## Pros and Cons of the Options

### Option 1: Direct Upload via User Delegation SAS (Chosen)

* Good, because it offloads network I/O and memory pressure entirely to Azure Blob Storage.
* Good, because credentials are short-lived, signed by Entra ID Managed Identity, and blob-scoped.
* Good, because Defender for Storage integrates natively with Event Grid for automated isolation.
* Bad, because client applications must handle the Azure Blob HTTP `PUT` specification.

### Option 2: Multipart Form-Data via API

* Good, because clients can submit survey data and files in a single HTTP request.
* Bad, because buffering large files in memory or ephemeral disk exhausts container resources.
* Bad, because gateway timeouts abort slow uploads.

### Option 3: Account-Key SAS

* Good, because SAS tokens can be generated offline without an Entra ID round-trip.
* Bad, because master storage account keys violate DfE zero-trust security policies and create credential leakage hazards.

## More Information

Implementation:
* `src/EstateDataCollection.Infrastructure/Storage/AzureBlobStorageService.cs`
* `src/EstateDataCollection.Infrastructure/Configuration/BlobStorageOptions.cs`
