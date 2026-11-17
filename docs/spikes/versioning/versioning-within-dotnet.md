# Versioning within .Net

The `Asp.Versioning` library (formerly `Microsoft.AspNetCore.Mvc.Versioning`) is highly extensible and supports nearly every standard pattern for API versioning.

- [dotnet/aspnet-api-versioning](https://github.com/dotnet/aspnet-api-versioning): Provides a set of libraries which add service API versioning to ASP.NET Core Minimal APIs, Controllers, gRPC, OData, and Web API (Classic).

When configuring versioning in ASP.NET Core, an open-source web development framework | .NET Core, there are two distinct concepts you must define:

1. **Version Styles (Readers)**: How does the *client* tell the server which version they want?
2. **Conventions**: How does the *server* know which version a controller/endpoint belongs to?

## Version Styles (Readers)

You configure how the application reads the incoming version by setting the `ApiVersionReader`.

```csharp
builder.Services
    .AddApiVersioning(options =>
    {
        // [...]
        options.ApiVersionReader = ApiVersionReader.Combine(
            // List of "Readers"
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("X-Api-Version")
        );
    });
```

### Query String

If you do not specify a reader, this is what the library uses by default. The version is passed as a query parameter.

**Client Request**

```http
GET /api/users/1?api-version=2.0
```

**Configuration**

```csharp
options.ApiVersionReader = new QueryStringApiVersionReader("api-version");
// "api-version" is the default query parameter name, but you can change it.
```

### URL Segment

The version is baked directly into the URL path. This is the most visible and easily cacheable approach.

**Client Request**

```http
GET /api/v2.0/users/1
```

**Configuration**

```csharp
options.ApiVersionReader = new UrlSegmentApiVersionReader();
```

*Note: This requires you to update your route templates on controllers to include the version token, e.g., `[Route("api/v{version:apiVersion}/[controller]")]`.*

### Headers

The version is passed in a custom HTTP header. This keeps the URL clean and represents the resource accurately without version pollution in the path.

**Client Request**

```http
GET /api/users/1
X-Api-Version: 2.0
```

**Configuration**

```csharp
options.ApiVersionReader = new HeaderApiVersionReader("X-Api-Version");
```

### Media Type (Content Negotiation)

The version is passed inside the standard HTTP `Accept` header. This is considered the most "RESTful" approach by purists, as it versions the *representation* rather than the resource itself.

**Client Request**

```http
GET /api/users/1
Accept: application/json;v=2.0
```

**Configuration**

```csharp
options.ApiVersionReader = new MediaTypeApiVersionReader("v");
// "v" is the default query parameter name, but you can change it, but not recommended.
```

### Combined

Finally continuing from the introduction, you can support multiple methods simultaneously. For example, you might allow URL segments for easy browser testing, but require Headers for official SDK integrations.

**Configuration**

```csharp
options.ApiVersionReader = ApiVersionReader.Combine(
    new UrlSegmentApiVersionReader(),
    new HeaderApiVersionReader("X-Api-Version"),
    new QueryStringApiVersionReader("api-version")
);
```

## Conventions

This is how controllers declare its versions; conventions dictate how you map a specific C# Controller (or individual Action method) to an API version.

If a request comes in for `v2.0`, the framework uses these conventions to figure out which code to execute.

### Attribute-Based Convention

This is the default [and so more declarative] and most explicit way to version. You apply attributes directly to your classes and methods. It is excellent for smaller APIs where everything is contained in a few files.

**Key Attributes**

- `[ApiVersion("1.0")]`: Declares that the controller *supports* version 1.0.
- `[MapToApiVersion("1.0")]`: Declares that a specific action *only* responds to version 1.0.
- `[ApiVersionNeutral]`: Declares that the endpoint opt-outs of versioning entirely (e.g., a `/health` or `/metrics` endpoint). It accepts any version or no version at all.

> [!NOTE]
> A little note on Date-Based/Group Versioning: `Asp.Versioning` natively supports date-based versions out-of-the-box via attributes. You can simply write `[ApiVersion("2026-07-01")]` and the framework will parse and route it natively.

**Example of Implementation**

```csharp
// 1. Declare all versions this controller handles
[ApiVersion("1.0", Deprecated = true)] // Mark older versions as deprecated right in the attribute
[ApiVersion("2.0")]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    // 2. Map specific actions to specific versions
    [HttpGet]
    [MapToApiVersion("1.0")]
    public IActionResult GetV1() => Ok("Version 1 Response");

    [HttpGet]
    [MapToApiVersion("2.0")]
    public IActionResult GetV2() => Ok("Version 2 Response");

    // 3. Shared actions (no [MapTo...] attribute)
    // Because the controller supports 1.0 and 2.0, this action will be executed for both versions
    [HttpDelete("{id}")]
    public IActionResult Delete(int id) => NoContent();
}

// 4. Version-Neutral Controllers
[ApiVersionNeutral]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Ping() => Ok("pong");
}
```

**Summary**

| Pro | Cons |
| --- | --- |
| Easy to understand and implement for small, simple APIs. | Leads to "attribute soup" in larger applications. Mixing the business logic of multiple versions in a single file makes the code incredibly hard to read and increases the risk of accidental breaking changes. |

### Centralised Convention

This uses a Fluent API builder that gives you the exact same power as attributes.

If you do not want to pollute your controllers with `[ApiVersion]` attributes—perhaps because you are retrofitting versioning onto an existing, massive API codebase and don't want to modify hundreds of files—you can define all your mappings centrally in `Program.cs`.

**Configuration**

```csharp
builder.Services
    .AddApiVersioning()
    .AddMvc(options =>
    {
        // Configure V1 Controller
        options.Conventions.Controller<UsersControllerV1>()
            .HasDeprecatedApiVersion(new ApiVersion(1, 0))
            .HasApiVersion(new ApiVersion(1, 1));

        // Configure V2 Controller with Action-level mapping
        options.Conventions.Controller<UsersControllerV2>()
            .HasApiVersion(new ApiVersion(2, 0))
            .Action(c => c.GetSpecialV2Feature()) // Target a specific method
                .MapToApiVersion(new ApiVersion(2, 0));

        // Configure Version Neutral
        options.Conventions.Controller<HealthController>()
            .IsApiVersionNeutral();
    });
```

**Summary**

| Pro | Cons |
| --- | --- |
| Relies on strongly-typed references via the Controllers. | This means the `Program.cs` file must know about every single controller in your application. This can become an enormous, merge-conflict-prone file in large teams. |

### Namespace Convention

To avoid the "attribute soup" of declarative routing and the massive configuration file of centralized routing, the library provides a built-in convention that derives the API version directly from the controller's .NET namespace.

**How it works**

The built-in `VersionByNamespaceConvention` uses a regular expression to search the namespace for standard semantic version patterns: `v{Major}` or `v{Major}_{Minor}`.

| Controller Namespace | Extracted Version |
| --- | --- |
| `App.Controller.v1.UsersController` | `1.0` |
| `App.Controller.v2_1.UsersController` | `2.1` |
| `App.Controller.v3.UsersController` | `3.0` |
| `App.Controller.UserController` | `Ignored`. It can’t find anything from the mentioned patterned above. |

This keeps it clearer where you’d have the below folder structure

```text
Controllers/
├── v1/
│   ├── AdminController.cs
│   └── UsersController.cs
├── v2/
│   └── AdminController.cs
└── v3/
    └── UsersController.cs
```

Which allows you to keep versions and their controllers isolated and visible without having to have the version within the controller via attribution. The only thing to note about this approach that the above example will only have `AdminController` available on `V1 & V2`, whilst `UserController` is available on `V1 & V3`.

**Configuration**

```csharp
builder.Services
    .AddApiVersioning()
    .AddMvc(options =>
    {
        options.Conventions.Add(new VersionByNamespaceConvention());
    });
```

**Summary**

| Pro | Con |
| --- | --- |
| Keeps the controllers completely clean of versioning metadata. | As the API grows, the central configuration file becomes massive, tedious to maintain, and a frequent source of merge conflicts. |

> [!IMPORTANT]
> **Important Note on Date-Based (Group) Versioning**
>
> While the `Asp.Versioning` library perfectly supports date-based versions natively (via attributes like `[ApiVersion("2026-07-01")]`), the **built-in `VersionByNamespaceConvention` does not**.
>
> The regex inside `VersionByNamespaceConvention` is hardcoded to only look for numeric segments (like `v1`). It will completely ignore a namespace like `MyApp.Controllers.V2026_07_01`.
>
> If you want to combine **Date-based** versions with **Namespace routing**, you must write a Custom Convention.

### Custom Conventions

The framework exposes the `IControllerConvention` interface, which allows you to write entirely custom rules for how your API applies versions. This is the ultimate escape hatch.

When ASP.NET Core boots up, it passes a `ControllerModel` (which describes the controller class, its namespace, and its attributes) to the custom convention. You can inspect this model and inject version information dynamically.

**Common Uses**

Here is a list what seems to be a modern / common approach to setting up environments, especially outfacing api’s compared to framework packages. These finding come from versioning implementation done by some SAAS organisations like Stripe, see here for more information: [Introduction to the principles of Versioning | Something to note](./introduction-to-the-principles-of-versioning.md#something-to-note).

1. **Date-based Namespace Routing**: Writing a custom regex to parse `V2026_07_01` from the namespace (since the built-in one won't do it).
2. **Carry-Forward Routing**: Automatically registering older controllers to handle newer versions if an overriding controller doesn't exist (the Stripe model).
3. **Global Deprecation**: Automatically marking any controller in a `V1` namespace as deprecated without having to touch the code.

**Example**

Here is an example of a version deprecator convention.

```csharp
public class DeprecateV1Convention : IControllerConvention
{
    public bool Apply(IControllerConventionBuilder controller, ControllerModel controllerModel)
    {
        // Inspect the controller's namespace
        var ns = controllerModel.ControllerType.Namespace ?? string.Empty;

        // Apply logic, this will mutate the controller's versioning metadata
        if (ns.Contains(".V1"))
        {
            var v1 = new ApiVersion(1, 0);

            // Make sure that the controller has been flagged with the v1 metadata
            controller.HasApiVersion(v1);

            // Marks it deprecated globally
            controller.HasDeprecatedApiVersion(v1);

            // Tells the framework we successfully applied a convention
            return true;
        }

        // Did not apply anything
        return false;
    }
}
```

And then configure.

```csharp
options.Conventions.Add(new DeprecateV1Convention());
```

This allows you to automatically flag all your controllers within given namespaces as deprecated without having to update each one individually.

**Summary**

There is no true Pros vs. Cons for a custom convention, apart from you start having custom implementations of versioning compared to the standard industry approaches.

## Further Reading

Here is a list of items which are pretty good for further reading:

1. [API Versioning in ASP.NET Core | What is API Versioning?](https://www.milanjovanovic.tech/blog/api-versioning-in-aspnetcore)
2. [GitHub - dotnet/aspnet-api-versioning](https://github.com/dotnet/aspnet-api-versioning): Provides a set of libraries which add service API versioning to ASP.NET Core Minimal APIs, Controllers, gRPC, OData, and Web API (Classic).
