---
status: "superseded by ADR-0003 and ADR-0004"
status: "implemented"
date: 2026-10-21
decision-makers: ["Chris Kelly"]
---

# 13. Superseding Relational SQL Document Cache in Favour of Pure Blob Storage Correlation

## Context and Problem Statement

During initial architectural spikes (recorded in historic ADR 0002), an append-only relational database (**Azure SQL Database** 
/ `edc` Document Cache) was used to store incoming JSON survey payloads, submission metadata, contract candidate verifications, 
audit events, and state transitions. Data access was implemented using **Dapper** with **Microsoft Entra ID Managed Identity** 
token injection (`https://database.windows.net/.default`).

While Dapper and Azure SQL provided sub-millisecond mapping and clean parameterisation, several operational and architectural 
challenges emerged as the service matured:
1. **Schema Impedance Mismatch**: Survey payloads are highly dynamic and vary significantly between survey collections (e.g., 
   CDC1, CDC2, Net Zero). Storing varying JSON documents inside relational database columns underutilised relational query 
   capabilities while incurring relational storage costs.
2. **Operational Overhead & Cost**: Provisioning and managing Azure SQL instances (or Elastic Pools) across multiple environments 
   introduced significant infrastructure costs and maintenance requirements (DDL migrations, connection pooling, firewall rules).
3. **Payload Immutability**: Survey submissions are staged drafts that, once finalised, become immutable historical records 
   destined for downstream ingestion into the Common Data Environment (CDE). They do not require complex relational joins or 
   transactional rollbacks across entities.

How should EDC persist raw survey submissions and coordinate draft lifecycles without relational schema overhead and operational 
database costs?

## Decision Drivers

* **Zero Schema Impedance Mismatch:** Ingestion store must natively accept variable JSON documents without DDL migrations.
* **Serverless Cost Efficiency:** Eliminate high fixed compute costs of idle relational database engines.
* **High-Throughput Immutability:** Append-only, write-once storage with deterministic correlation and natural audit trails.
* **Decoupled Downstream Projections:** Ingestion should focus purely on collection and verification, leaving downstream 
  relational projections to CDE consumers.

## Considered Options

* **Option 1: Pure Multi-Container Azure Blob Storage Correlation (`schemas`, `payloads`, `processed`)**
* **Option 2: Relational SQL Document Cache with Dapper (Historic ADR 0002)**
* **Option 3: Document Database (Azure Cosmos DB)**

## Decision Outcome

Chosen option: **"Option 1: Pure Multi-Container Azure Blob Storage Correlation"**, because it provides serverless 
scalability, zero database migrations, and dramatic cost reduction while satisfying all draft correlation requirements:

1. **Blob Storage as Primary Ingestion Store**:
   - `schemas`: Authoritative contract store for JSON Schema Draft 2020-12 specifications.
   - `payloads`: Staging zone for in-progress draft survey JSON documents.
   - `processed`: Immutable archive of verified, finalised survey envelopes tagged with SHA-256 integrity hashes and URN 
     metadata.
2. **Correlation via Deterministic Naming & UUIDv7**:
   Correlation between schemas, drafts, and finalised records is maintained through deterministic blob naming paths 
   (`{schemaName}/v{version}.json`, `{submissionId}.json`, `{urn}/{year}/{submissionId}.json`) and time-sortable UUIDv7 
   identifiers.
3. **Downstream CDE Ingestion**:
   Downstream relational projections, reporting aggregations, and data warehouse ingestion are decoupled from the ingestion 
   service, allowing downstream services to pull finalised envelopes directly from the `processed` container.

### Consequences

* Good, because expensive Azure SQL compute and licensing costs are eliminated in favour of low-cost serverless Blob Storage.
* Good, because adding new survey fields, changing validation rules, or deprecating questions requires zero SQL DDL migrations 
  or maintenance windows.
* Good, because Blob Storage eliminates connection pool exhaustion and scales automatically to thousands of concurrent surveyor 
  submissions.
* Good, because finalised envelopes in `processed` provide a natural, tamper-evident audit log with SHA-256 integrity hashes.
* Bad, because querying in-progress drafts by complex arbitrary attributes (e.g., "find all drafts submitted between 2 PM and 
  3 PM by local authority X") cannot be queried via SQL `SELECT` statements (mitigated by storing metadata tags on blobs or 
  relying on downstream CDE indexing).

### Confirmation

Compliance is confirmed via:
- Retirement of SQL connection strings and DDL migration scripts from the primary ingestion pipeline.
- End-to-end integration tests verifying complete submission lifecycle against Azure Blob Storage.
- Clean isolation of draft staging in `payloads` and promotion to `processed`.

## Pros and Cons of the Options

### Option 1: Pure Multi-Container Blob Storage (Chosen)

* Good, because it provides virtually unlimited horizontal scaling at low cost.
* Good, because it eliminates relational database maintenance and connection pooling.
* Good, because JSON payloads are stored natively without serialization translation.
* Bad, because complex ad-hoc querying requires downstream search or data warehouse indexing.

### Option 2: Relational SQL Document Cache with Dapper (Historic)

* Good, because SQL `SELECT` queries and transactional constraints are readily available.
* Good, because Dapper provides fast object mapping without ORM overhead.
* Bad, because managing relational tables for dynamic JSON creates schema impedance and high database provisioning costs.
* Bad, because connection pool limits constrain massive burst scale during nationwide collection deadlines.

### Option 3: Document Database (Azure Cosmos DB)

* Good, because it natively stores JSON with flexible indexing and SQL-like querying.
* Bad, because Request Unit (RU/s) pricing introduces significant ongoing costs for bulk staging.
* Bad, because multi-region replication and fine-grained indexing are unnecessary for staged drafts destined for CDE.

## More Information

Superseded ADR:
* Historic ADR 0002 (`docs/adr/_historic/0002-data-access-with-dapper-and-entra-id-managed-identity.md`)
Superseding ADRs:
* [ADR 0003: Multi-Container Blob Storage Landing Zone](0003-multi-container-blob-storage-landing-zone.md)
* [ADR 0004: Session-Based Draft Ingestion, Schema Version Pinning, and UUIDv7](0004-session-based-draft-ingestion-and-schema-pinning.md)
