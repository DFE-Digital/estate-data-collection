---
status: "accepted"
sub-status: "implemented"
date: 2026-11-17
decision-makers: ["Chris Kelly"]
---

# 18. Zero-Logic Razor Views and Presentation ViewModel Pattern

## Context and Problem Statement

As the Admin UI grew across multiple pages (`DashboardPage`, `SchemaDetailPage`, `SchemaVersionsPage`, `ScheduleSchemaPage`, `WorkbenchPage`, `SchemaTestsPage`) and reusable GOV.UK components (`SchemaSectionCard`, `IdentifierConfigFieldset`, `VerifyResultPanel`, `SchemaFieldTree`, `LifecycleTag`, `NotificationBanner`, `ErrorSummary`), Razor templates accumulated inline `@{ ... }` computation blocks, `@code` helper methods (JSON formatting, field error matching, constraint formatting), LINQ sorting/filtering, URL escaping, date formatting, and nested conditional expressions for CSS classes and status tags.

Mixing presentation logic into `.razor` templates makes UI conditionals opaque to fast unit tests (forcing full `HtmlRenderer` string-scraping to test edge cases) and blurs responsibilities between state calculation and HTML structure. How should presentation state and conditionals be structured across `EstateDataCollection.Admin.UI`?

## Decision Drivers

* **Dumb, Declarative Views:** `.razor` pages and components should serve purely as structural HTML templates reading precomputed properties, boolean flags, and projected item ViewModels.
* **Direct Unit Testability:** Every visibility condition, CSS tag/modifier choice, field error binding, URL construction, and JSON/date formatting rule must be directly testable in C# without rendering HTML.
* **Null-Safety & Edge-Case Resilience:** Avoid runtime `NullReferenceException`s inside Razor markup (for example when `SchemaVersionsViewModel.Schema` is `null` on a `404` failure).
* **Backward Compatibility:** Preserve top-level page ViewModel constructor signatures where routes instantiate them cleanly.

## Considered Options

* **Option 1: Zero-Logic `.razor` Templates + Comprehensive Presentation ViewModels (`*ViewModel`)**
* **Option 2: Code-Behind `.razor.cs` Partial Classes Holding Rendering Helpers**
* **Option 3: Inline C# Expressions and `@code` Helpers Inside `.razor` Files**

## Decision Outcome

Chosen option: **"Option 1: Zero-Logic `.razor` Templates + Comprehensive Presentation ViewModels"**, establishing the following rules across [`EstateDataCollection.Admin.UI`](../../src/EstateDataCollection.Admin.UI):

1. **Purely Declarative `.razor` Files:**
   - Razor pages (`Pages/*.razor`), layouts (`Layouts/*.razor`), and components (`Components/*.razor`) contain **zero** `@{ ... }` computation blocks, **zero** `@code` helper methods (only `[Parameter]` declarations), **zero** LINQ/JSON/date/URL operations, and **zero** string comparisons or ternary/switch expressions.
   - Markup binds strictly to `@Model.*` / `@item.*` properties, boolean flags (`@if (Model.ShowSoftWarningBanner)`), and projected collections (`@foreach (var card in Model.InvalidFixtures)`).
2. **Rich Presentation ViewModels (`Models/ViewModels/*.cs`):**
   - Page ViewModels (`SchemaTestsViewModel`, `DashboardViewModel`, `SchemaDetailViewModel`, `ScheduleSchemaViewModel`, `WorkbenchViewModel`, `SchemaVersionsViewModel`) and component ViewModels (`IdentifierConfigFieldsetViewModel`, `SchemaSectionCardViewModel`, `WorkbenchVerifyViewModel`, `FieldsViewerViewModel`, `QualityRulesViewerViewModel`, `ErrorSummaryViewModel`, `NotificationBannerViewModel`, `LifecycleTagViewModel`, `AdminPageMeta`) own all presentation computation, CSS class resolution, date formatting, JSON indentation, URL escaping, and child component ViewModel projection.
   - `SchemaTestsViewModel` serves as the canonical example: `GetFieldError`, `FormatFixturesForEditor`, `FormatJson`, `InvalidFixtureParser.Parse`, `expectedErrors` assertion tagging, and soft-warning banner conditions live inside `SchemaTestsViewModel` and project into `FixtureEditorFieldViewModel`, `ValidFixtureCardViewModel`, `InvalidFixtureCardViewModel`, and `ExpectedErrorAssertionViewModel`.

### Consequences

* Good, because `.razor` files remain concise, readable GOV.UK HTML templates with zero embedded business or formatting logic.
* Good, because presentation edge cases (error highlighting, soft-warning fallbacks, identifier label overrides, null-detail error screens) can be fast-unit-tested directly via C# assertions in [`ViewModelPresentationTests.cs`](../../test/EstateDataCollection.Tests.Unit/AdminUi/ViewModelPresentationTests.cs).
* Good, because refactoring or auditing Razor files can enforce a strict rule (`@code` blocks contain parameters only).
* Bad, because ViewModel classes carry slightly more boilerplate projection records (`*ItemViewModel`, `*CardViewModel`).

### Confirmation

Compliance is confirmed via:
- [`ViewModelPresentationTests.cs`](../../test/EstateDataCollection.Tests.Unit/AdminUi/ViewModelPresentationTests.cs) testing presentation logic on ViewModels directly.
- [`RazorComponentRenderingTests.cs`](../../test/EstateDataCollection.Tests.Unit/AdminUi/RazorComponentRenderingTests.cs) verifying full static SSR rendering for all pages and components.
- Auditing `src/EstateDataCollection.Admin.UI/{Pages,Components,Layouts}/*.razor` to ensure zero `@{` blocks, zero helper methods, and zero inline LINQ/formatting logic.

## More Information

Related ADRs:
* [ADR 0008: Testing Strategy](0008-testing-strategy-awesomeassertions-nsubstitute-and-fixtures.md)
* [ADR 0017: Server-Side Rendered Admin Portal & Proxy-Only API Boundary](0017-ssr-admin-portal-and-proxy-boundary.md)
