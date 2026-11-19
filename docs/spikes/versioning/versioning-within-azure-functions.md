# Versioning within Azure Functions (Isolated Worker)

This spike builds on [Introduction to the principles of Versioning](./introduction-to-the-principles-of-versioning.md) and [Versioning within .Net](./versioning-within-dotnet.md), adapting the **CalVer + Namespace convention** pattern specifically for an **Azure Functions v4 Isolated Worker** environment (`Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore`).

---

## Why `Asp.Versioning` Does Not Apply Directly

In standard ASP.NET Core, `Asp.Versioning.Mvc` and `Asp.Versioning.Http` hook into MVC `ControllerModel` discovery and ASP.NET Core's `EndpointDataSource` routing table.

In Azure Functions v4 Isolated Worker:

1. **The Functions Host owns HTTP routing:** Build-time tooling generates `functions.metadata` from `[Function]` and `[HttpTrigger]` attributes. The Azure Functions Host matches incoming HTTP requests to a single function entry point *before* dispatching the invocation to the .NET worker.
2. **Duplicate routes are rejected:** You cannot register two `[HttpTrigger]` functions on the same HTTP method and route template (e.g., two `GET api/schemas` functions in `V2026_11_19` and `V2027_02_01`) and rely on `Asp.Versioning` attributes or conventions to disambiguate them.
3. **Conventions never execute against `[HttpTrigger]`:** `IControllerConvention` and `VersionByNamespaceConvention` only inspect MVC controllers or Minimal API route builders, not `[Function]` classes.

> [!NOTE]
> Even though `ConfigureFunctionsWebApplication()` integrates ASP.NET Core's `HttpContext` into the isolated worker pipeline, route selection still happens in the Functions Host prior to worker middleware execution.

---

## Separating the Three "Versions" in This Codebase

Before versioning any endpoint, keep these three distinct concepts separate:

| Concept | Format | Purpose | Example |
| --- | --- | --- | --- |
| **Build / Artifact CalVer** ([ADR 0015](../../decisions/0015-calendar-versioning-calver-and-log-enrichment.md)) | `YYYY.0M.<run_number>` | Tracing deployed binaries, logs, OpenTelemetry, and `GET /api/about` | `2026.10.42+a1b2c3d` |
| **API Contract CalVer** | `YYYY-MM-DD` (ISO date) | Managing breaking HTTP contract changes over time (Stripe model) | `2026-11-19` |
| **Domain Resource Version** | Integer in resource URL | Selecting a stored schema contract revision inside the domain | `/schemas/{name}/versions/2` |

- **Build CalVer** should never be used as an API contract version (every CI run would mint a new version even when nothing changed, and 3-part dot numbers are not standard API version tokens).
- **Version-neutral routes** such as SSR UI pages/assets (`Routes/Admin/*` under `/manage` and `/assets/{*assetPath}`) and health/metadata endpoints (`GET /api/about`) should opt out of API contract versioning entirely.

---

## Option 1: URL-Segment CalVer with Namespace Folders (Simplest in Functions)

Each breaking-change milestone gets its own namespace folder and explicit date-prefixed `[HttpTrigger]` route.

### Folder Structure

```text
Routes/
├── Admin/                              # Version-neutral SSR pages & static assets
│   ├── DashboardPageEndpoint.cs
│   └── ...
└── Api/
    ├── V2026_11_19/
    │   ├── GetAdminSchemasEndpoint.cs  # Route = "api/2026-11-19/schemas"
    │   └── SubmitSchemaEndpoint.cs     # Route = "api/2026-11-19/schemas"
    └── V2027_02_01/
        └── SubmitSchemaEndpoint.cs     # Route = "api/2027-02-01/schemas"
```

### Characteristics

| Pros | Cons |
| --- | --- |
| 100% native to Azure Functions (`functions.metadata` stays unambiguous). | No automatic carry-forward: `2027-02-01` will not resolve `2026-11-19` endpoints unless you add thin forwarding triggers or an Azure API Management (APIM) rewrite rule. |
| Preserves the 1-class / 1-`[Function]` `FunctionEndpointBase` + `IFunctionEndpoint` architecture untouched. | Version is baked into every client URL rather than negotiated via headers. |
| Cacheable by HTTP proxies, CDNs, and APIM out of the box. | Creating a full `/2027-02-01/` surface without APIM means re-exposing unchanged endpoints. |

---

## Option 2: Single `[HttpTrigger]` Dispatcher + `X-Api-Version` Middleware + Namespace Carry-Forward (Stripe Model in Functions)

If you want **clean URLs** (`/api/schemas`), **header/query negotiation** (`X-Api-Version: 2026-11-19`), and **true carry-forward** (only writing code in a new `VYYYY_MM_DD` namespace for endpoints with breaking changes), separate the **Function trigger (dispatcher)** from the **versioned handler**.

### 1. Folder & Namespace Layout

```text
Routes/
├── Admin/                                  # Version-neutral SSR pages & assets
│   ├── DashboardPageEndpoint.cs
│   └── ...
└── Api/
    ├── GetAdminSchemasEndpoint.cs          # Single [Function] trigger on GET api/schemas
    ├── SubmitSchemaEndpoint.cs             # Single [Function] trigger on POST api/schemas
    ├── V2026_11_19/                        # Baseline API version (2026-11-19)
    │   ├── GetAdminSchemasHandler.cs
    │   └── SubmitSchemaHandler.cs
    └── V2027_02_01/                        # Breaking change only for SubmitSchema
        └── SubmitSchemaHandler.cs          # GET api/schemas automatically carries forward V2026_11_19
```

### 2. Versioned Handler Contract & Namespace Parser

Because the built-in `Asp.Versioning` `VersionByNamespaceConvention` only matches `v{Major}` / `v{Major}_{Minor}`, parse `VYYYY_MM_DD` directly from each handler's namespace:

```csharp
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace EstateDataCollection.Common.Web.Versioning;

public interface IVersionedEndpointHandler<in TRequest>
{
    Task<IActionResult> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

public static partial class ApiVersionNamespaceParser
{
    [GeneratedRegex(@"\.V(?<year>\d{4})_(?<month>\d{2})_(?<day>\d{2})(?:\.|$)")]
    private static partial Regex VersionPattern();

    public static DateOnly? TryParse(Type handlerType)
    {
        var match = VersionPattern().Match(handlerType.Namespace ?? string.Empty);
        if (!match.Success)
        {
            return null;
        }

        return new DateOnly(
            int.Parse(match.Groups["year"].Value),
            int.Parse(match.Groups["month"].Value),
            int.Parse(match.Groups["day"].Value));
    }
}
```

### 3. Scrutor Discovery + Carry-Forward Resolution

During startup (alongside `AddFunctions()` in `EstateDataCollection.Common.Web`), scan all `IVersionedEndpointHandler<>` implementations and build a catalog keyed by `(HandlerContract, DateOnly)`:

- **Resolution rule (Carry-Forward):** For a requested version $D$, pick the handler for that endpoint whose namespace date is the **greatest $V \le D$**.
  - Requesting `2026-11-19` on `POST /api/schemas` resolves `V2026_11_19.SubmitSchemaHandler`.
  - Requesting `2027-02-01` on `POST /api/schemas` resolves `V2027_02_01.SubmitSchemaHandler`.
  - Requesting `2027-02-01` on `GET /api/schemas` (unchanged in `2027-02-01`) carries forward to `V2026_11_19.GetAdminSchemasHandler`.
- **Omitted version:** Decide explicitly per host policy whether omitting `X-Api-Version` defaults to the **latest active version** (convenient for internal tools) or **requires a version / pins to the earliest active contract** (safer for external SDK integrations).

### 4. `IFunctionsWorkerMiddleware` for Reading & Reporting Versions

Because `GlobalExceptionMiddleware` and `RequestSizeLimitMiddleware` already inspect `context.GetHttpContext()`, an `ApiVersionMiddleware` fits into the same pipeline:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace EstateDataCollection.Common.Web.Middleware;

public sealed class ApiVersionMiddleware(IApiVersionCatalog catalog) : IFunctionsWorkerMiddleware
{
    public const string HttpContextItemKey = "ApiVersion";

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext is null || IsVersionNeutral(httpContext.Request.Path))
        {
            // Non-HTTP triggers (e.g. BlobTrigger) and SSR UI/metadata routes pass straight through
            await next(context);
            return;
        }

        var rawVersion = httpContext.Request.Headers["X-Api-Version"].FirstOrDefault()
                         ?? httpContext.Request.Query["api-version"].FirstOrDefault();

        if (!catalog.TryResolve(rawVersion, out var resolvedVersion, out var errorProblem))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(
                errorProblem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: httpContext.RequestAborted);
            return;
        }

        httpContext.Items[HttpContextItemKey] = resolvedVersion;
        httpContext.Response.Headers["api-supported-versions"] = string.Join(", ", catalog.SupportedVersions);

        if (catalog.DeprecatedVersions.Count > 0)
        {
            httpContext.Response.Headers["api-deprecated-versions"] = string.Join(", ", catalog.DeprecatedVersions);
        }

        await next(context);
    }

    private static bool IsVersionNeutral(PathString path) =>
        path.StartsWithSegments("/manage") ||
        path.StartsWithSegments("/assets") ||
        path.StartsWithSegments("/api/about");
}
```

### 5. Thin `[HttpTrigger]` Dispatcher + Versioned Handler

The `[Function]` class stays a single-operation dispatcher on the canonical route:

```csharp
namespace EstateDataCollection.AzureFunctions.Admin.Routes.Api;

public sealed class GetAdminSchemasEndpoint(
    IVersionedHandlerResolver resolver
) : IFunctionEndpoint
{
    public const string EndpointName = "GetAdminSchemas";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Get), Route = "api/schemas")]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        var version = http.HttpContext.Items[ApiVersionMiddleware.HttpContextItemKey] as DateOnly?;
        var handler = resolver.Resolve<IVersionedEndpointHandler<HttpRequest>>(
            endpointKey: EndpointName,
            requestedVersion: version);

        return handler.HandleAsync(http, cancellationToken);
    }
}
```

And the versioned handler extends `FunctionEndpointBase` with all binding, mapping, and instrumentation isolated inside its date namespace:

```csharp
namespace EstateDataCollection.AzureFunctions.Admin.Routes.Api.V2026_11_19;

public sealed class GetAdminSchemasHandler(
    IUseCase<GetSchemasResult, GetSchemasRequest> useCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation), IVersionedEndpointHandler<HttpRequest>
{
    public Task<IActionResult> HandleAsync(HttpRequest http, CancellationToken cancellationToken) =>
        ExecuteAsync(
            GetAdminSchemasEndpoint.EndpointName,
            useCase,
            () => Task.FromResult(Bind(http)),
            StatusCodes.Status200OK,
            cancellationToken: cancellationToken);
}
```

### 6. Architecture & Integration Testing Impact

When adopting Option 2 in this repository:

- Update `FunctionEndpointAndEventArchitectureTests` to also assert that every `IVersionedEndpointHandler<>` is `sealed`, lives in a namespace matching `ApiVersionNamespaceParser`, and resolves cleanly from DI.
- `IntegrationEndpointHarness` can either invoke the dispatcher with `http.HttpContext.Items[ApiVersionMiddleware.HttpContextItemKey] = new DateOnly(2026, 11, 19)` or resolve/test a specific `VYYYY_MM_DD` handler directly.

---

## Option 3: Custom `Asp.Versioning` (`IControllerConvention`) — Only Outside `[HttpTrigger]`

If the HTTP surface were ever moved off Azure Functions triggers onto an ASP.NET Core Controllers / Minimal API host (keeping Azure Functions strictly for Blob/Queue background processing), then `Asp.Versioning.Mvc` + a custom `IControllerConvention` becomes viable:

```csharp
using System.Text.RegularExpressions;
using Asp.Versioning;
using Asp.Versioning.Conventions;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

public sealed partial class CalVerNamespaceConvention : IControllerConvention
{
    [GeneratedRegex(@"\.V(?<year>\d{4})_(?<month>\d{2})_(?<day>\d{2})(?:\.|$)")]
    private static partial Regex VersionPattern();

    public bool Apply(IControllerConventionBuilder controller, ControllerModel controllerModel)
    {
        var ns = controllerModel.ControllerType.Namespace ?? string.Empty;

        if (ns.Contains(".Routes.Admin", StringComparison.Ordinal))
        {
            controller.IsApiVersionNeutral();
            return true;
        }

        var match = VersionPattern().Match(ns);
        if (!match.Success)
        {
            return false;
        }

        var date = new DateOnly(
            int.Parse(match.Groups["year"].Value),
            int.Parse(match.Groups["month"].Value),
            int.Parse(match.Groups["day"].Value));

        controller.HasApiVersion(new ApiVersion(date));
        return true;
    }
}
```

This matches the custom convention approach described in [Versioning within .Net](./versioning-within-dotnet.md#custom-conventions), but is **not** compatible with `[HttpTrigger]` endpoints.

---

## Summary Comparison for Azure Functions

| Criterion | Option 1: URL Segment (`api/2026-11-19/...`) | Option 2: Dispatcher + `X-Api-Version` Middleware | Option 3: `Asp.Versioning` (`IControllerConvention`) |
| --- | --- | --- | --- |
| Works with `[HttpTrigger]` | **Yes** (native) | **Yes** (native) | **No** (requires ASP.NET Core host) |
| Namespace-driven CalVer (`V2026_11_19`) | Yes | Yes | Yes |
| Automatic carry-forward (Stripe style) | Only via APIM rewrite or alias triggers | **Yes** (built into DI resolver) | Via custom convention / gateway |
| Clean URLs without version segment | No | **Yes** | Yes |
| Standard `api-supported-versions` headers | Manual / APIM | **Yes** (via middleware) | Built-in |
| Complexity / boilerplate | Lowest | Moderate | N/A for Functions |

### Practical Guidance

- **Wait until the first breaking contract change** before introducing versioned folders—additive changes (new optional fields, new endpoints) do not require a new version.
- **For low-ceremony Functions projects or APIM-fronted APIs**, start with **Option 1** (`Routes/Api/V2026_11_19/` + `api/2026-11-19/...`).
- **For header-negotiated APIs without APIM** that want true Stripe-style carry-forward across many endpoints, adopt **Option 2** using an `IFunctionsWorkerMiddleware` + `IVersionedEndpointHandler<>` resolver in `Common.Web`.
