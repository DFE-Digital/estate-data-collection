# OpenAPI Documentation

API documentation tooling is the set of tools that turn your API's actual structure, its routes, request shapes, and responses, into something a developer can read, explore, and trust without opening your source code. At its core, choosing this tooling is about closing the gap between what your API does and what your docs say it does.

The moment a spec drifts out of sync with the running code, documentation stops helping developers and starts actively misleading them.

Here is a review of where that tooling currently stands for a .NET 10 environment.

## The basics

Here is a breakdown of common modern packages that are used within the .NET 10 workspace:

- Microsoft dropped **Swashbuckle** from the default ASP.NET Core project templates starting in .NET 9, moving to a first-party package, `Microsoft.AspNetCore.OpenApi`, for generating the OpenAPI spec itself.
- That package generates the spec (JSON/YAML) but has no UI of its own, you pair it with a separate renderer.
- **Scalar** (`Scalar.AspNetCore`) has emerged as the generally recommended default renderer for new .NET projects in 2026, a modern, interactive UI with a built-in "try it" client, replacing the aging Swagger UI.
- **Redoc** is the go-to alternative when you want a clean, read-only reference with no interactive console.
- **NSwag** is worth adding to the pipeline specifically when you need generated typed C#/TypeScript clients, not as a docs-UI choice on its own.
- **DocFX** is the option if you need to combine REST API reference with conceptual docs and in-repo class-library reference in a single site.

## The details

Here is a step-by-step detailed breakdown on that current landscape and possibilities for working with documentation

### Swashbuckle is no longer the default

Swashbuckle was the long-standing default for generating Swagger/OpenAPI docs in ASP.NET Core, but it ran into maintenance problems as core contributors stepped away. Starting with .NET 9, Microsoft moved away from Swashbuckle in the default project templates and introduced native support via the `Microsoft.AspNetCore.OpenApi` package instead. Swashbuckle itself isn't dead, version 10.x still supports .NET 10 with opt-in OpenAPI 3.1, but Microsoft no longer ships it in the default Web API template.

### `Microsoft.AspNetCore.OpenApi` Generator

This is now the first-party, built-in way to produce the OpenAPI document itself:

- Supports generating OpenAPI 3.1 documents starting in .NET 10 (3.2 from .NET 11), full JSON Schema draft 2020-12 support, generating documents at runtime via an endpoint, "transformer" APIs to modify the generated document, multiple documents from a single app, and native AOT compatibility.
- Supports viewing the generated OpenAPI document at runtime via a parameterized endpoint, and also supports generating the OpenAPI document at build time, with the output customizable via document transformers.
- .NET 10 changes what the generated document looks like too, e.g. nullable types are represented via JSON Schema's type-array form rather than a `nullable: true` flag, and XML-comment generation handles complex types better than in earlier .NET versions.
- Build-time generation is done via the companion `Microsoft.Extensions.ApiDescription.Server` package, which automatically generates the OpenAPI document during build, by default emitting it to the project's output directory as `{ProjectName}.json` (the filename is configurable).

This package only produces the spec, a UI or static site on top of it is a separate concern, covered next.

### Package renders

Here is a list of renders which turn the generated spec file(s) into a user friendly UI.

| Tool | What it is | Notes |
| --- | --- | --- |
| **Scalar** (`Scalar.AspNetCore`) | Modern interactive reference UI + built-in API test client | Described as the modern Swagger UI replacement, cleaner, faster, dark mode by default, with a request builder better than Swagger UI's. Generally recommended as the default choice for new projects in 2026, combining polished docs with a built-in client and code generation in one MIT-licensed package. Supports build-time static export via `scalar build`, which generates a static site that can be committed or deployed to a web server, plus a hosted CLI (`scalar project publish`) that can deploy from local disk or directly from a connected GitHub repo. |
| **Redoc / Redocly** | Clean, read-only three-panel reference renderer | Widely used (~1M weekly npm downloads), three-panel layout with search and code samples; generates a static site via `redoc-cli bundle`. Best suited to teams that want a polished reference with no interactive "try it" console. |
| **Swagger UI** (via Swashbuckle or NSwag) | The original OpenAPI renderer, "Try it out" console | Still the most widely used by download count (~3M/week) and the most familiar tool to developers already in the OpenAPI ecosystem, but Microsoft has removed it from the default .NET Core template starting in .NET 9. |
| **NSwag** | Full spec generation + Swagger UI + typed client (C#/TypeScript) SDK generation | A different category of tool from Scalar/Swashbuckle, a code generation platform that happens to include spec generation and a Swagger UI, with its primary value being generating typed C# and TypeScript clients directly from the OpenAPI spec so front-end/mobile/service clients don't drift out of sync. Its own Swagger UI is functional but less polished than Scalar, so teams often use NSwag purely for client generation and layer Scalar on top for the docs experience. |
| **DocFX** | .NET Foundation static site generator | Takes source code, Markdown articles, and REST API specifications as input and produces a self-contained static website (or PDF), distributed as a NuGet package/.NET tool. Can also generate .NET API docs directly from a built assembly plus its XML doc file. Good fit if the deliverable needs to combine REST API reference with conceptual Markdown pages and internal library docs in one site; more setup overhead than Scalar/Redoc for a pure REST reference. |
| **Docusaurus** + OpenAPI plugin | JS-based static site generator, not .NET-native | Builds the site into a directory of static content that can be copied to any static file host such as GitHub Pages, Vercel, or Netlify. Only worth considering if the team already runs a broader Docusaurus-based developer portal and wants the API reference embedded in it; otherwise it adds a Node toolchain the .NET-native options avoid. |

## 2026 state-of-the-market summary

Swagger UI is not outdated as a specification, but as a renderer it is showing its age, for a clean, free reference, Scalar and Redoc are the current leaders. For .NET specifically, a common recommended pairing is Scalar plus NSwag where client generation is also needed. Whichever of Scalar (modern UI), NSwag (code generation), or Redoc (documentation focus) a team picks, all represent a better experience than the old Swashbuckle-only setup.

## Primary recommendations

**`Microsoft.AspNetCore.OpenApi` (spec) + `Scalar.AspNetCore` (UI), with build-time generation and static publishing**, matching the Stripe/GitHub pattern of "spec is the source of truth, docs are a build artefact." See the companion implementation document for the working pipeline.

For further reading see [Recommended Generation & Publishing of API Docs](./recommended-generation-and-publishing-of-api-docs.md).

## Sources

- [What's new in ASP.NET Core in .NET 10](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0)
- [Generate OpenAPI documents](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi)
- [codewithmukesh - .NET 10 Tutorials, Benchmarks & Courses](https://codewithmukesh.com/)
- [ASP.NET Core Dropped Swagger - Here's What Replaced It in .NET 10](https://codewithmukesh.com/blog/aspnet-core-dropped-swagger-what-replaced-it/)
- Scalar vs Swashbuckle vs NSwag in ASP.NET Core 2026
- [Docsio: AI Documentation Generator](https://docsio.co/)
- Self-Hosted API Documentation Tools: Scalar vs Redoc vs RapiDoc 2026
- [GitHub - dotnet/docfx: Static site generator for .NET API documentation.](https://github.com/dotnet/docfx)
