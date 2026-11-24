---
status: "accepted"
sub-status: "implemented"
date: 2026-11-17
decision-makers: ["Chris Kelly"]
---

# 22. Authoring Schema Regression Test Fixtures and Expected-Error Assertions

## Context and Problem Statement

While [ADR 0020](0020-schema-regression-test-suites-and-soft-warnings.md) establishes the architecture for storing, running, and auditing schema regression tests, contract authors and future engineering teams need a canonical specification and concrete, copy-ready examples showing **how to structure valid and invalid test fixtures** for any data contract (including JSON Schema rules, `JsonLogic` / `CEL` quality rules, and `SchemaIdentifierConfig`).

Without clear conventions and examples, teams risk writing:
1. **Valid fixtures** that pass schema syntax in isolation but omit required identifiers or violate business-logic rules.
2. **Invalid fixtures** as unannotated raw payloads that only prove *something* failed (triggering soft warnings `"Failed — unknown if expected"`), rather than proving the **intended** business rule or structural constraint was enforced.
3. **Brittle or ambiguous wrapper objects** whose keys collide with domain payloads or whose `expectedErrors` strings do not match runtime error formatting.

How should teams author, annotate, verify, and promote valid and invalid test fixtures across the Contract Workbench, `SchemaTestsPage`, and Admin API?

## Decision Drivers

* **Single Golden Authoring Standard:** Provide one documented convention shared by the Contract Workbench (`/manage/workbench`), the per-version Test Suite editor (`/manage/schemas/{schemaName}/{version}/tests`), and the Admin API (`PUT /api/schemas/{schemaName}/versions/{version}/tests` / `POST /api/schemas/verify`).
* **Full Three-Stage Verification:** Every fixture must be documented and tested against all three verification stages exercised by [`BlobSchemaRegistry`](../../src/EstateDataCollection.Infrastructure/Registry/BlobSchemaRegistry.cs):
  1. **JSON Schema** (Draft-07 / Draft 2020-12 structure, types, enums, patterns, `additionalProperties`)
  2. **Quality / Business Logic Rules** (`JsonLogic` and `CEL` attached directly or compiled from ODCS v3.2.0 YAML)
  3. **Verified Identifier Extraction** (`URN`, custom pointer/label, or disabled `None`)
* **Deterministic Negative Testing:** Negative tests should specify `"expectedErrors"` so regressions (e.g. a rule silently passing while an unrelated typo causes a failure) are caught immediately.
* **Incremental Promotion Workflow:** Authors must be able to explore failures via soft warnings first, inspect the emitted diagnostic strings, and lock them down into `expectedErrors` assertions.

## Considered Options

* **Option 1: Standardise on Annotated Wrapper Fixtures (`{ description, payload, expectedErrors }`) with Exact Runtime Error Format Examples and Promotion Workflow**
* **Option 2: Separate Assertion Files Outside Fixture JSON Arrays**
* **Option 3: Leave Authors to Infer Error Strings Ad Hoc**

## Decision Outcome

Chosen option: **"Option 1: Standardise on Annotated Wrapper Fixtures (`{ description, payload, expectedErrors }`) with Exact Runtime Error Format Examples and Promotion Workflow"**.

---

## Standard Specification & Runtime Matching Rules

### 1. Valid Fixtures (`SampleValidFixtures`)

Valid fixtures are **raw domain payload JSON objects** (no wrapper). During verification and `RunSchemaTestsUseCase`, every valid fixture **must**:
1. Pass JSON Schema validation (`eval.IsValid == true`).
2. Pass all attached `JsonLogic` / `CEL` business rules (`logicResult.Value.IsValid == true`).
3. Extract a valid identifier when identifier extraction is enabled (`Status == Valid`, or `NotPresent` only when the identifier field is optional in the JSON Schema).

* **Admin UI input (`validFixtureJson`):** Enter either a single JSON object `{ ... }` or a JSON array `[ { ... }, { ... } ]` (leave blank or `[]` to clear).
* **Admin API (`PUT /api/schemas/{schemaName}/versions/{version}/tests`):** Pass `"validFixtures"` as an array of serialised JSON strings (`IReadOnlyList<string>`).

### 2. Invalid Fixtures (`SampleInvalidFixtures`)

Per [`InvalidFixtureParser`](../../src/EstateDataCollection.Domain/Schema/InvalidFixtureParser.cs), each entry in the invalid fixture set may use one of two shapes:

1. **Annotated Expectation Wrapper (Recommended):**
   ```json
   {
     "description": "Human-readable explanation of what this negative case proves",
     "payload": { "...": "full domain JSON object" },
     "expectedErrors": ["RULE-CONDITION-GRADE-D"]
   }
   ```
   Supported wrapper keys (case-insensitive; **no other top-level keys** may exist alongside them):
   - `"payload"` *(required)*: The domain JSON object (or JSON string) to evaluate.
   - `"expectedErrors"` *(recommended)*: Array of expected substrings (or single string `"expectedError"`).
   - `"description"` *(or `"name"`, optional)*: Label displayed in `SchemaTestsPage` and `VerifyResultPanel` cards.

   > [!NOTE]
   > Because [`InvalidFixtureParser`](../../src/EstateDataCollection.Domain/Schema/InvalidFixtureParser.cs) checks that *every* top-level key belongs to `{ payload, expectedErrors, expectedError, description, name }`, a domain payload that legitimately has a `"payload"` field alongside domain fields (e.g. `{"payload": {"value": 1}, "school": {"urn": "123456"}}`) is safely parsed as a **bare domain payload**, not misread as a wrapper.

2. **Bare Domain Payload (Legacy / Exploratory Mode):**
   Any raw `{ ... }` object without wrapper keys runs through full validation, fails if it unexpectedly passes, and emits a **non-blocking soft warning** (`"Failed — unknown if expected"`) listing all triggered errors so the author can promote them to `expectedErrors`.

### 3. Exact Triggered Error Format Reference

[`BlobSchemaRegistry`](../../src/EstateDataCollection.Infrastructure/Registry/BlobSchemaRegistry.cs) prefixes every triggered error by stage so assertions can target either the **stage + rule ID** or a specific **instance JSON pointer**:

| Validation Stage | Runtime Triggered Error Format | Recommended `expectedErrors` Token(s) |
| :--- | :--- | :--- |
| **Business Rule (`JsonLogic` / `CEL`)** | `[Rule:<RuleId>] <propertyPath>: <message>` | `"RULE-CONDITION-GRADE-D"` or `"[Rule:RULE-CONDITION-GRADE-D]"` + optional path/message |
| **JSON Schema Structure / Constraint** | `[Schema] <instanceLocation>: <errorMessage>` | `"[Schema] /elements"` or `"required"` / property name |
| **Verified Identifier Extraction** | `[Identifier] Missing required '<Type>' identifier at '<pointer>'.` or `[Identifier] <errorMessage>` | `"[Identifier]"` + `"/school/urn"` |
| **Malformed Payload JSON** | `[JSON] Could not parse payload: <exceptionMessage>` | `"[JSON]"` |

**Matching semantics:**
* Each entry in `expectedErrors` is matched via **case-insensitive substring comparison** (`StringComparison.OrdinalIgnoreCase`) against the list of `TriggeredErrors`.
* **AND semantics:** *Every* entry in `expectedErrors` must match at least one triggered error for that fixture. If even one expected token is missing (`UnmatchedExpectedErrors.Count > 0`), verification fails hard (`Passed = false`).
* **Targeted isolation tip:** Invalid payloads testing a **specific business rule** (e.g. `RULE-CONDITION-GRADE-D`) should remain **structurally valid** against JSON Schema and have a valid identifier — otherwise schema errors could mask whether the business rule was actually evaluated.

---

## Concrete Examples (Using `cdc_survey` and Generic Contracts)

### Example 1: Valid Fixture for `cdc_survey`

A minimal valid payload satisfies all JSON Schema required sections (`survey`, `school`, `sites`, `elements`, `answers`, `evidence`), satisfies the identifier pointer (`/school/urn`), and obeys every JsonLogic rule (`RULE-CONDITION-GRADE-D`, `RULE-REPAIR-COST-CALCULATION`, `RULE-SITE-ANSWER-VALIDATION`, etc.):

```json
[
  {
    "survey": {
      "id": "SURV-1234-5678",
      "survey_name": "CCD",
      "participant_id": "abc-def-ghi",
      "surveyor_email": "surveyor@supplier.com",
      "is_complete": true,
      "created_at": "2026-10-15T10:00:00Z",
      "updated_at": "2026-10-15T14:30:00Z"
    },
    "school": {
      "urn": "123456",
      "name": "Example School"
    },
    "sites": [
      {
        "name": "EFA1",
        "blocks": [
          {
            "name": "EFAA",
            "type": "block",
            "floors": [{ "name": "EFAA-GF" }]
          }
        ]
      }
    ],
    "elements": [
      {
        "id": "ELM-001",
        "NRM3_code": "25.10.10",
        "description": "Windows",
        "asset_id": "EFA1",
        "asset_type": "site",
        "occurrences": [
          {
            "id": "OCC-001",
            "name": "Main Building Windows",
            "basis_of_assessment": "Seen",
            "condition_grade": "C",
            "condition_description": "Minor weathering",
            "replacement": {
              "rem_life": 7,
              "uom": "m2",
              "replacement_quantity": 400,
              "replacement_rate": 1000.0
            },
            "repairs": [
              {
                "id": "REP-001",
                "repair_description": "Replace 20no. Double Glazed Units",
                "repair_item": "Double Glazed Unit Replacement",
                "repair_classification": "Y",
                "uom": "No.",
                "repair_quantity": 20,
                "repair_rate": 400.0,
                "estimated_cost": 8000.0
              }
            ]
          }
        ]
      }
    ],
    "answers": [
      {
        "id": "ANS-001",
        "question_id": "Q-101",
        "asset_id": "EFA1",
        "asset_type": "site",
        "value": "Yes",
        "value_type": "lookup_list",
        "created_at": "2026-10-15T11:00:00Z",
        "updated_at": "2026-10-15T11:00:00Z",
        "evidence": [{ "id": "EVD-001" }]
      }
    ],
    "evidence": [
      {
        "id": "EVD-001",
        "asset_id": "ELM-001",
        "asset_type": "element",
        "description": "Photograph showing window frame wear",
        "mime_type": "image/jpeg",
        "blob_key": "s3://dfe-surveys/evidence/EVD-001.jpg",
        "file_origin": "iPad Camera",
        "created_at": "2026-10-15T11:02:00Z",
        "updated_at": "2026-10-15T11:02:00Z"
      }
    ]
  }
]
```

### Example 2: Annotated Invalid Fixtures Covering Business Rules, Schema Structure & Identifiers

Paste a JSON array like the following into the **Invalid test payload JSONs** editor on `SchemaTestsPage` (or Contract Workbench):

```json
[
  {
    "description": "Grade D occurrence without any repairs triggers RULE-CONDITION-GRADE-D",
    "payload": {
      "survey": {
        "id": "SURV-1234-5678",
        "survey_name": "CCD",
        "participant_id": "abc-def-ghi",
        "is_complete": true,
        "created_at": "2026-10-15T10:00:00Z",
        "updated_at": "2026-10-15T14:30:00Z"
      },
      "school": { "urn": "123456", "name": "Example School" },
      "sites": [{ "name": "EFA1", "blocks": [] }],
      "elements": [
        {
          "id": "ELM-001",
          "NRM3_code": "25.10.10",
          "description": "Windows",
          "asset_id": "EFA1",
          "asset_type": "site",
          "occurrences": [
            {
              "id": "OCC-001",
              "name": "Main Building Windows",
              "condition_grade": "D",
              "replacement": {
                "rem_life": 7,
                "uom": "m2",
                "replacement_quantity": 400,
                "replacement_rate": 1000.0
              },
              "repairs": []
            }
          ]
        }
      ],
      "answers": [],
      "evidence": []
    },
    "expectedErrors": [
      "RULE-CONDITION-GRADE-D",
      "At least one repair must be provided"
    ]
  },
  {
    "description": "Repair estimated_cost mismatch triggers RULE-REPAIR-COST-CALCULATION",
    "payload": {
      "survey": {
        "id": "SURV-1234-5678",
        "survey_name": "CCD",
        "participant_id": "abc-def-ghi",
        "is_complete": true,
        "created_at": "2026-10-15T10:00:00Z",
        "updated_at": "2026-10-15T14:30:00Z"
      },
      "school": { "urn": "123456", "name": "Example School" },
      "sites": [{ "name": "EFA1", "blocks": [] }],
      "elements": [
        {
          "id": "ELM-001",
          "NRM3_code": "25.10.10",
          "description": "Windows",
          "asset_id": "EFA1",
          "asset_type": "site",
          "occurrences": [
            {
              "id": "OCC-001",
              "name": "Main Building Windows",
              "condition_grade": "C",
              "replacement": {
                "rem_life": 7,
                "uom": "m2",
                "replacement_quantity": 400,
                "replacement_rate": 1000.0
              },
              "repairs": [
                {
                  "id": "REP-001",
                  "repair_quantity": 20,
                  "repair_rate": 400.0,
                  "estimated_cost": 9999.0
                }
              ]
            }
          ]
        }
      ],
      "answers": [],
      "evidence": []
    },
    "expectedErrors": [
      "[Rule:RULE-REPAIR-COST-CALCULATION]"
    ]
  },
  {
    "description": "Missing required top-level schema sections fails JSON Schema validation",
    "payload": {
      "survey": {
        "id": "SURV-1234-5678",
        "survey_name": "CCD",
        "participant_id": "abc-def-ghi",
        "is_complete": false,
        "created_at": "2026-10-15T10:00:00Z",
        "updated_at": "2026-10-15T10:00:00Z"
      },
      "school": { "urn": "123456" }
    },
    "expectedErrors": [
      "[Schema]",
      "elements"
    ]
  },
  {
    "description": "Missing required URN triggers identifier extraction failure",
    "payload": {
      "survey": {
        "id": "SURV-1234-5678",
        "survey_name": "CCD",
        "participant_id": "abc-def-ghi",
        "is_complete": true,
        "created_at": "2026-10-15T10:00:00Z",
        "updated_at": "2026-10-15T14:30:00Z"
      },
      "school": { "name": "School Without URN" },
      "sites": [],
      "elements": [],
      "answers": [],
      "evidence": []
    },
    "expectedErrors": [
      "[Identifier]",
      "/school/urn"
    ]
  }
]
```

### Example 3: Updating Fixtures via the Admin API (`PUT /api/schemas/{schemaName}/versions/{version}/tests`)

When automating fixture updates in CI/CD or `.http` scripts, serialise each fixture entry (including wrapper objects) as a JSON string element inside `"validFixtures"` and `"invalidFixtures"`:

```http
PUT {{adminBaseUrl}}/schemas/cdc_survey/versions/1/tests
Content-Type: application/json
x-functions-key: {{adminFunctionKey}}

{
  "validFixtures": [
    "{\"survey\":{\"id\":\"SURV-1234-5678\",\"survey_name\":\"CCD\",\"participant_id\":\"abc-def-ghi\",\"is_complete\":true,\"created_at\":\"2026-10-15T10:00:00Z\",\"updated_at\":\"2026-10-15T14:30:00Z\"},\"school\":{\"urn\":\"123456\",\"name\":\"Example School\"},\"sites\":[],\"elements\":[],\"answers\":[],\"evidence\":[]}"
  ],
  "invalidFixtures": [
    "{\"description\":\"Missing elements fails JSON Schema\",\"payload\":{\"survey\":{\"id\":\"SURV-1234-5678\",\"survey_name\":\"CCD\",\"participant_id\":\"abc-def-ghi\",\"is_complete\":false,\"created_at\":\"2026-10-15T10:00:00Z\",\"updated_at\":\"2026-10-15T10:00:00Z\"},\"school\":{\"urn\":\"123456\"}},\"expectedErrors\":[\"[Schema]\",\"elements\"]}"
  ]
}
```

---

## Step-by-Step Authoring Workflow for Teams

1. **Start from a known-good Valid Fixture:** Author at least one complete payload in **Contract Workbench** (`/manage/workbench`) or on `/manage/schemas/{schemaName}/{version}/tests` and verify it passes Schema, Business Rules, and Identifier Extraction together.
2. **Clone Valid Payloads to Create Focused Invalid Variants:** Mutate **only the single field or relationship** needed to trigger the target rule or constraint. This avoids accidental structural errors shadowing business-rule evaluation.
3. **Discover Exact Error Strings via Soft Warnings (Optional):** If you are unsure of the exact error string a new rule emits, submit the raw payload without `"expectedErrors"`. Both `VerifyResultPanel` and `SchemaTestsPage` mark it `"Failed — unknown if expected"` (a non-blocking soft warning) and display the exact `TriggeredErrors` badges (e.g. `[Rule:RULE-CONDITION-GRADE-D] /elements/0/occurrences/0/repairs: ...`).
4. **Promote to Strict Assertions:** Wrap the fixture in `{"description": "...", "payload": {...}, "expectedErrors": ["RULE-..."]}` and click **Save & Verify Tests**. Confirm the card flips from yellow (`Failed — unknown if expected`) to green (`Matched expected error(s)`) and no soft warnings remain.
5. **Re-run Across All Live Schemas:** Use **Run All Schema Tests** on the dashboard (or `POST /api/schemas/tests/run` in release pipelines) and check the `Tests` column on [`SchemaSectionCard`](../../src/EstateDataCollection.Admin.UI/Components/SchemaSectionCard.razor) for green `Passed` tags across versions.

### Consequences

* Good, because future contract authors have copy-ready templates covering JSON Schema, `JsonLogic`/`CEL` business rules, identifier failures, and Admin API serialisation.
* Good, because mutating a valid baseline payload + asserting `expectedErrors` prevents silent false-positive negative tests.
* Neutral, because legacy bare fixtures remain non-blocking via soft warnings while teams progressively add `expectedErrors`.

### Confirmation

Fixture parsing, matching semantics, UI rendering, and test-state reporting are verified by:
- [`InvalidFixtureParserTests.cs`](../../test/EstateDataCollection.Tests.Unit/Domain/InvalidFixtureParserTests.cs)
- [`BlobSchemaRegistryVerifyTests.cs`](../../test/EstateDataCollection.Tests.Unit/Infrastructure/BlobSchemaRegistryVerifyTests.cs)
- [`RunSchemaTestsUseCaseTests.cs`](../../test/EstateDataCollection.Tests.Unit/UseCases/RunSchemaTestsUseCaseTests.cs) & [`UpdateSchemaTestsUseCaseTests.cs`](../../test/EstateDataCollection.Tests.Unit/UseCases/UpdateSchemaTestsUseCaseTests.cs)
- [`RazorComponentRenderingTests.cs`](../../test/EstateDataCollection.Tests.Unit/AdminUi/RazorComponentRenderingTests.cs)

## More Information

Related ADRs:
* [ADR 0002: Dynamic JSON Schema Validation](0002-dynamic-json-schema-contract-validation.md)
* [ADR 0008: Testing Strategy with NSubstitute, AwesomeAssertions, and Reusable Fixtures](0008-testing-strategy-awesomeassertions-nsubstitute-and-fixtures.md)
* [ADR 0019: Configurable Schema-Verified Identifier Extraction & Blob Partitioning](0019-configurable-schema-verified-identifier-partitioning.md)
* [ADR 0020: Stored Schema Regression Test Suites, Expected-Error Assertions, and Soft Warnings](0020-schema-regression-test-suites-and-soft-warnings.md)
