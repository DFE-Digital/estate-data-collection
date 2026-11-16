# CDE Data Contracts

Repository for storing Open Data Contract Standard (ODCS) contracts, generated JSON Schemas, sample payloads, data quality rules, and validation assets.

## Overview

This repository follows a contract-first approach to defining and validating data exchanged within CDE services.

Data contracts are the source of truth and are used to:

- Define data structures
- Define validation rules
- Define business quality rules using JSONLogic
- Generate JSON Schemas automatically
- Support downstream validation and ingestion processes

---

## Repository Structure

```text
.
├── contracts/
│   └── Data contract files (.odcs.yaml)
│
├── schemas/
│   └── Generated JSON Schema files (.schema.json)
│
├── samples/
│   └── Example payloads
│
├── validation/
│   └── Validation utilities
│
└── .github/
    └── workflows/
        └── generate-schema.yml
```

| Folder | Purpose |
|----------|----------|
| `contracts/` | Stores ODCS data contracts that act as the source of truth. |
| `schemas/` | Stores generated JSON Schemas. |
| `samples/` | Stores example payloads used for testing and documentation. |
| `validation/` | Stores validation scripts and utilities. |
| `.github/workflows/` | Stores GitHub Actions workflows used for contract validation and schema generation. |

---

## Current Contracts

| Contract | Generated Schema | Description |
|----------|------------------|-------------|
| `edc-school-site-jsonlogic.odcs.yaml` | `edc-school-site.schema.json` | School site survey submission contract including structural validation and JSONLogic quality rules. |
| `edc-survey-evidenceandanswers.odcs.yaml` | `edc-survey-evidenceandanswers.schema.json` | Survey evidence and answers submission contract. |
| `edc-survey-elements.odcs.yaml` | `edc-survey-elements.schema.json` | Survey elements submission contract. |

---

## Automated Schema Generation

JSON Schemas are automatically generated from ODCS contracts using GitHub Actions and `datacontract-cli`.

The workflow performs the following steps:

```text
ODCS Contract
      ↓
Validate Contract
      ↓
Generate JSON Schema
      ↓
Commit Schema
      ↓
Push Changes to Repository
```

The JSON Schema should never be edited manually.

Any change to a schema must be made by updating the corresponding contract in the `contracts/` directory.

---

## GitHub Actions

### Workflow

`generate-schema.yml`

### Purpose

The workflow automatically validates ODCS contracts and generates JSON Schemas.

### Workflow Steps

1. Check out the repository.
2. Install `datacontract-cli`.
3. Validate the contract.
4. Generate the JSON Schema.
5. Commit updated schema files back to the repository when changes are detected.

---

## Workflow Triggers

The Generate JSON Schema workflow runs automatically whenever an ODCS contract in the `contracts/` directory is added or updated.

The workflow can also be executed manually from GitHub Actions.

### Trigger Behaviour

| Change | Workflow Runs |
|----------|----------|
| Contract updated | ✅ |
| New contract added | ✅ |
| Schema updated by workflow | ❌ |
| README updated | ❌ |
| Workflow updated | ❌ |
| Manual execution | ✅ |


---
