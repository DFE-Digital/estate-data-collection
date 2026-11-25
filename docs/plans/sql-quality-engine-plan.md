# Implementation Plan: SQL Quality Validation Engine (`type: sql` / `engine: sql`)

- **Status:** Deferred / Separate Spike & Design Plan (Plugs into `IQualityRuleEngine` / `IWorkflowStep`)
- **Related Plans:**
  - [ODCS Schema & Multi-Engine Quality Plan](odcs-schema-and-quality-engines-plan.md)
  - [Validation Workflow Engine Plan](validation-workflow-engine-plan.md)

---

## 1. Context & Standards Landscape (ODCS + .NET)

The data team generates ODCS contracts from Databricks / Open Metadata and wants **`sql`** supported alongside `jsonlogic`, `cel`, and `unique`.

### Is There a Single SQL Standard?
- **At the ODCS Contract Schema level — YES:**
  - ODCS v3.x natively defines `type: sql` quality rules containing:
    - `query` (or `implementation`) holding a SQL `SELECT` query (often referencing table/object placeholders like `${object}` / `${table}`)
    - Assertion bounds such as `mustBe: 0`, `mustNotBe`, `mustBeGreaterThan`, `mustBeLessThan`, `mustBeBetween`, etc.
    - Optional metadata fields (`engine` / `dialect`, `description`, `severity`, `dimension`).
- **At the SQL Dialect & Execution Engine level — NO single universal engine:**
  - ODCS does **not** standardise a single SQL dialect; rules authored in Databricks often use **Spark SQL / Databricks SQL** syntax, whereas an in-process .NET service does not have Spark available locally.
  - Because JSON submissions are **hierarchical trees** (`survey`, `elements[].occurrences[].repairs[]`) rather than flat relational tables, an in-process SQL engine also needs a **deterministic JSON-to-Relational projection** (or native JSON SQL functions) AND strict **read-only sandboxing**.

---

## 2. Candidate In-Process SQL Engines for .NET (Azure Functions Flex Consumption)

| Option | Engine / Package | Dialect Compatibility | JSON / Nested Array Support | Operational & Runtime Trade-offs |
| :--- | :--- | :--- | :--- | :--- |
| **Option A (Recommended for Full SQL)** | **DuckDB (`DuckDB.NET.Data.Full`)** | High ANSI SQL + Postgres + many Spark/Databricks-like functions (`TRY_CAST`, `REGEXP_MATCHES`, `COALESCE`, CTEs, window functions, `QUALIFY`) | Native JSON reading (`read_json` / `UNNEST` / struct & list traversal) or fast per-request in-memory relational tables | Embedded C++ native library (`linux-x64`, `osx-arm64`, `win-x64`). Verify native binary size & memory caps on Azure Functions Flex Consumption. |
| **Option B (Lightest Footprint)** | **SQLite (`Microsoft.Data.Sqlite`) `:memory:`** | SQLite SQL dialect (differs slightly from Spark SQL—no `QUALIFY`, custom `REGEXP` UDF needed, `strftime` vs `date_format`) | Built-in JSON1 (`json_extract`, `json_each`) + C# projection into temp tables | Tiny footprint, battle-tested in .NET across all OS/Flex Consumption targets; higher dialect-translation burden if data team writes Databricks SQL. |
| **Option C (Zero Native Deps — Row Predicates Only)** | **Managed SQL Parser (`SqlParserCS` / `TSqlParser` AST → Evaluator)** | Subset of `WHERE` expressions only | Evaluates per node via `RuleTargetResolver` | Cannot execute `JOIN`s, `GROUP BY`, `HAVING`, window functions, or cross-array aggregations (`SUM`, `COUNT` across child rows). |
| **Option D (Not Recommended for Hot Path)** | **Remote Databricks SQL Warehouse Pushdown** | 100% Databricks SQL | Native variant/struct | Adds 500ms–10s+ latency, external auth/network failure modes, and cost per draft save; violates low-latency stateless validation design. |

**Recommendation:** Prototype **Option A (DuckDB in-process)** vs **Option B (SQLite `:memory:` with JSON1 + Spark-compatible UDF shims)** in a quick spike on the Flex Consumption `linux-x64` publish target before committing.

---

## 3. Data Mapping & Query Execution Model

### 3.1 Relational Table Projection from Hierarchical Payload
Given a nested JSON payload, expose predictable relational views/tables to SQL rules without mutating source data:
- Root object fields → `payload` / `root` (and top-level schema object name from ODCS, e.g., `survey` or `submission`)
- Nested arrays along `rule.Path` (or all schema-defined array entities) → flattened tables with synthetic foreign keys:
  - `elements` (`_element_idx`, fields...)
  - `occurrences` (`_element_idx`, `_occurrence_idx`, parent fields or FK join, occurrence fields...)
  - `repairs` (`_element_idx`, `_occurrence_idx`, `_repair_idx`, repair fields...)
  - `ambient_reference_*` / `ambient_cross_submission` tables (read-only views injected from Phase 3 enrichers!)
- Support ODCS template placeholders:
  - `${table}` / `${object}` → resolved to the target table corresponding to `rule.Path`
  - `${property}` → resolved when a quality rule is attached to a specific column/property in ODCS YAML.

### 3.2 Supported Rule Modes in `SqlQualityEngine : IQualityRuleEngine`
Support the three standard ODCS SQL patterns cleanly via `BusinessRule`:
1. **ODCS Metric Assertion Mode** (`mustBe`, `mustBeLessThan`, etc. specified on rule):
   - Executes `SELECT COUNT(*) ...` (scalar result in row 0, col 0) and checks it against the assertion (e.g., `mustBe: 0`).
2. **Violation-Row Mode** (query returns failing rows / pointers):
   - Query selects non-compliant rows (including `_json_pointer` / synthetic index columns if present); 0 rows = pass, `> 0` rows = fail with exact JSON pointers mapped back into `ValidationError`.
3. **Target-Scoped Boolean Predicate Mode** (`WHERE`-clause expression scoped to `rule.Path`):
   - Runs as `SELECT _json_pointer FROM <target_table> WHERE NOT (<predicate>)` in a single set-based query rather than row-by-row N+1 queries!

### 3.3 Security Sandboxing & Guardrails
Untrusted SQL must **never** be executed unsandboxed, even though schemas come from Admin/ODCS pipelines:
- **AST / Keyword Allow-List Pre-Check (`ValidateSyntax`):**
  - Parse SQL at schema `Verify` / `Ingest` time using `SqlParserCS` (or DuckDB/SQLite authorizer callback).
  - Allow **only** single-statement `SELECT` / `WITH (...) SELECT` read queries.
  - Explicitly reject `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `CREATE`, `ATTACH`, `DETACH`, `PRAGMA`, `COPY`, `INSTALL`, `LOAD`, and external file/HTTP table functions (`read_csv`, `read_parquet`, `httpfs`, etc.).
- **Runtime Isolation:**
  - Fresh isolated in-memory instance per evaluation, external access/extensions disabled (`enable_external_access = false` in DuckDB / `SQLITE_DBCONFIG_ENABLE_LOAD_EXTENSION = 0` in SQLite).
  - Hard execution timeout (e.g., `SqlTimeoutMs = 2000` configurable in `ValidationOptions`) and max violation rows cap (`SqlMaxViolationsPerRule`).

---

## 4. Integration Points with Existing Architecture

1. **`OdcsContractParser`:**
   - Already recognises `type: sql`; extend extraction to preserve ODCS `query`, `dialect`, `mustBe` / threshold assertion properties, and map column-level `${property}` / `${object}` placeholders.
2. **`ContractConsistencyAnalyzer` (`POST /schemas/verify` & Ingest):**
   - Validate SQL syntax, enforce read-only AST security rules, verify referenced tables/columns exist in the compiled JSON Schema, and warn if Databricks-specific functions are used that fail on the configured local SQL dialect.
3. **`SqlQualityEngine` (`engine: "sql"`):**
   - Implements `IQualityRuleEngine`, plugs straight into `QualityRuleCoordinator` (`IBusinessLogicValidator`), participates automatically in `RuleSeverity`, stage/runbook ordering (`Stage`, `Order`, `DependsOn`), `issues` dictionary grouping, and `ValidationEngineTrace`.

---

## 5. Questions to Confirm with the Data Team

1. **Query Convention:** Will generated ODCS `type: sql` rules use metric assertions (`SELECT COUNT(*) ...` + `mustBe: 0`) or violation queries (`SELECT ... WHERE <bad>`)?
2. **Dialect Scope:** Can Databricks / Open Metadata contract generation emit ANSI/DuckDB-compatible SQL, or which Databricks SQL functions (e.g., `RLIKE`, `DATEDIFF`, `ARRAY_CONTAINS`) are must-haves?
3. **JSON Hierarchy:** Should nested arrays (`elements[].occurrences[].repairs[]`) be flattened into relational tables (`elements`, `occurrences`, `repairs` with synthetic keys) or queried as JSON paths?
