# Recommended Generation & Publishing of API Docs

Three steps, matching the Stripe/GitHub build-time pattern:

1. **Generate** the OpenAPI spec at build time (`Microsoft.AspNetCore.OpenApi` + `Microsoft.Extensions.ApiDescription.Server`).
2. **Render** the spec into a static site (`scalar build`).
3. **Publish** that static site via CI/CD (GitHub Actions → GitHub Pages, or your static host of choice).

No documentation is generated on the fly at request time in production, it's a build artefact, published like any other static site.

## Step one: Generate

Generate the OpenAPI documentation at build time. Here is some of the boilerplate configuration which is required to allow the project to generate at build time.

```xml
<!-- .csproj -->

<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.*" />
  <PackageReference Include="Microsoft.Extensions.ApiDescription.Server" Version="10.0.*">
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
</ItemGroup>

<PropertyGroup>
  <OpenApiGenerateDocuments>true</OpenApiGenerateDocuments>
  <OpenApiGenerateDocumentsOnBuild>true</OpenApiGenerateDocumentsOnBuild>
  <OpenApiDocumentsDirectory>$(MSBuildProjectDirectory)/openapi</OpenApiDocumentsDirectory>
  <OpenApiGenerateDocumentsOptions>--file-name openapi --openapi-version OpenApi3_1</OpenApiGenerateDocumentsOptions>
</PropertyGroup>
```

```csharp
// Program.cs

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Collect Census API",
            Version = "v1",
            Description = "Get status of a establishment's census"
        };

        return Task.CompletedTask;
    });
});

var app = builder.Build();
// JSON at /openapi/v1.json
app.MapOpenApi();
// Or, optional YAML
app.MapOpenApi("/openapi/{documentName}.yaml");
// Serves the interactive UI at /scalar
app.MapScalarApiReference();
```

`dotnet build` now emits `openapi.json` to the output directory as a build artefact, the equivalent of Stripe's/GitHub's committed spec file.

Now `dotnet build` drops `openapi/openapi.json` into your project directory as a plain build artifact, no running app required. This is the file you'd commit, diff in PRs, lint with Spectral, or publish, exactly like Stripe's `stripe/openapi` repo or GitHub's generated data files.

> [!NOTE]
> One thing worth flagging: if you already have `AddOpenApi()` wired for runtime generation, don't also add a Swashbuckle-based generator alongside `Microsoft.Extensions.ApiDescription.Server`, the two generators fight over the same MSBuild hooks and you silently get no output. Pick one generator (the native one here); the build-time package just re-runs that same generator at compile time.

## Step two: Render

Now let's render the spec into a static site. You only need this if you don't consume the `.MapScalarApiReference()` call as that will automate this dependency.

```bash
npm install -g @scalar/cli
scalar build ./bin/Release/net10.0/openapi.json --output ./docs-site
```

A little side note, you can swap for `redoc-cli bundle` if the read-only Redoc layout is preferred instead, see the [tooling review doc](./openapi-documentation.md) for the trade-offs.

## Step three: Publish

Here is a AI example on what could be used to whenever a commit happens into a main branch, it will automatically update the generated files onto GitHub Pages.

```yaml
# .github/workflows/publish-docs.yml

name: Publish API Docs
on:
  push:
    branches: [main]

jobs:
  build-and-publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'
      - run: dotnet build --configuration Release
      - uses: actions/setup-node@v4
        with:
          node-version: 22
      - run: npx @scalar/cli document validate ./bin/Release/net10.0/openapi.json
      - run: npx @scalar/cli build ./bin/Release/net10.0/openapi.json --output ./docs-site
      - uses: actions/upload-pages-artifact@v3
        with:
          path: ./docs-site
      - uses: actions/deploy-pages@v4
```

This publishes to GitHub Pages; swap the last two steps for Azure Static Web Apps / internal static hosting as needed.

If you want docs that can be published independently of the running API — e.g. a static docs site built entirely in CI, no app process needed to view them — serve the generated static file rather than the live one:

```csharp
// Program.cs

// [...]

// Serves /openapi/openapi.json from wwwroot, or use MapGet to return the file
app.UseStaticFiles();

// Add the location of where the spec will be location
app.MapScalarApiReference(options =>
{
    options.WithOpenApiRoutePattern("/openapi/openapi.json");
});
```

## Further decisions

Here is a few items which we need to decide on when publishing documentation files:

- **Interactive vs read-only**
  Scalar ships an in-browser "try it" request client. If that's undesirable for a public-facing reference (e.g. security/compliance concerns about hitting production from the docs page), Redoc's read-only renderer, or disabling Scalar's client, is the safer default. Companies like Stripe, emulate or allow for sandbox environments to be set up to trial their integrations.
- **Internal-only APIs**
  For internal services, it may be simpler to skip the static-site step entirely and just serve `/openapi/v1.json` plus a lightweight Scalar UI at runtime in non-prod environments, and disable the UI entirely in production, exposing an interactive UI in production can give attackers free reconnaissance on the API surface.

## Sources

- Build-Time Generation
- [Generate OpenAPI documents](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi)
- [What's new in ASP.NET Core in .NET 10](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0)
- [codewithmukesh - .NET 10 Tutorials, Benchmarks & Courses](https://codewithmukesh.com/)
- [ASP.NET Core Dropped Swagger - Here's What Replaced It in .NET 10](https://codewithmukesh.com/blog/aspnet-core-dropped-swagger-what-replaced-it/)
