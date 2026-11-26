using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.AzureFunctions.Admin.Middleware;
using EstateDataCollection.AzureFunctions.Admin.Options;
using EstateDataCollection.AzureFunctions.Admin.Routes.Admin;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Admin.Security;
using EstateDataCollection.Common.Security;
using EstateDataCollection.Common.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using msOptions = Microsoft.Extensions.Options.Options;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Admin;

public sealed class AdminAuthMiddlewareTests
{
    private static readonly FunctionAuthMetadataResolver AdminMetadataResolver =
        new(typeof(GetDashboardPage).Assembly);

    private static AdminAuthMiddleware CreateMiddleware(AdminAuthOptions? options = null)
    {
        return new AdminAuthMiddleware(
            msOptions.Create(options ?? new AdminAuthOptions { Enabled = true }),
            NullLogger<AdminAuthMiddleware>.Instance,
            AdminMetadataResolver);
    }

    private static string EncodeEasyAuthPrincipal(
        string userDetails,
        string[]? roles = null,
        string? oid = null,
        string? tid = null,
        string? displayName = null)
    {
        var claims = new List<object>
        {
            new { typ = "preferred_username", val = userDetails }
        };

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            claims.Add(new { typ = "name", val = displayName });
        }
        else
        {
            claims.Add(new { typ = "name", val = userDetails });
        }

        if (!string.IsNullOrWhiteSpace(oid))
        {
            claims.Add(new { typ = "oid", val = oid });
        }

        if (!string.IsNullOrWhiteSpace(tid))
        {
            claims.Add(new { typ = "tid", val = tid });
        }

        if (roles is not null)
        {
            foreach (var role in roles)
            {
                claims.Add(new { typ = "roles", val = role });
            }
        }

        var payload = JsonSerializer.Serialize(new
        {
            auth_typ = "aad",
            name_typ = "name",
            role_typ = "roles",
            userDetails,
            claims
        });

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    [Fact]
    public async Task InvokeHttpAsync_When_Disabled_Should_Pass_Through_And_Still_Populate_User_And_UserContext()
    {
        var middleware = CreateMiddleware(new AdminAuthOptions { Enabled = false });
        var metadata = AdminMetadataResolver.ResolveByFunctionName(GetDashboardPage.EndpointName);
        var userContext = new UserContext();
        var context = new DefaultHttpContext();
        context.Request.Path = "/manage";
        context.Request.Headers[EasyAuthPrincipalParser.ClientPrincipalHeader] =
            EncodeEasyAuthPrincipal(
                "alex.operator@education.gov.uk",
                ["Edc.Admin.Operator"],
                oid: "oid-alex-123",
                tid: "tid-dfe-999",
                displayName: "Alex Operator");

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, userContext, metadata);

        nextCalled.Should().BeTrue();
        context.User.Identity?.IsAuthenticated.Should().BeTrue();
        context.User.Identity?.Name.Should().Be("Alex Operator");

        userContext.IsAuthenticated.Should().BeTrue();
        userContext.UserId.Should().Be("oid-alex-123");
        userContext.TenantId.Should().Be("tid-dfe-999");
        userContext.Email.Should().Be("alex.operator@education.gov.uk");
        userContext.DisplayName.Should().Be("Alex Operator");
        userContext.AuthenticationType.Should().Be("aad");
        userContext.HasRole("Edc.Admin.Operator").Should().BeTrue();
        userContext.AuditUserDisplay.Should().Be("alex.operator@education.gov.uk (oid-alex-123)");
    }

    [Fact]
    public async Task InvokeHttpAsync_AllowAnonymous_Asset_Endpoint_Should_Bypass_Auth()
    {
        var metadata = AdminMetadataResolver.ResolveByFunctionName(GetStaticAssetEndpoint.EndpointName);
        metadata.AllowAnonymous.Should().BeTrue();

        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/assets/css/dfe-branding.css";

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, metadata: metadata);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeHttpAsync_Page_Get_Unauthenticated_Should_Redirect_To_Entra_Login()
    {
        var metadata = AdminMetadataResolver.ResolveByFunctionName(GetSchemaVersionsPage.EndpointName);
        metadata.AllowAnonymous.Should().BeFalse();
        metadata.RoleRequirements.Should().ContainSingle(r => r.Contains(AdminRoleKeys.Operator));

        var middleware = CreateMiddleware();
        var userContext = new UserContext();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/manage/schemas/cdc_survey";
        context.Request.QueryString = new QueryString("?notice=ok");

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, userContext, metadata);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        context.Response.Headers.Location.ToString()
            .Should().Be("/.auth/login/aad?post_login_redirect_uri=%2Fmanage%2Fschemas%2Fcdc_survey%3Fnotice%3Dok");
        userContext.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeHttpAsync_Page_Post_Unauthenticated_Should_Return_401()
    {
        var metadata = AdminMetadataResolver.ResolveByFunctionName(PostWorkbenchVerifyEndpoint.EndpointName);
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/manage/workbench/verify";

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, metadata: metadata);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeHttpAsync_Inherited_FunctionPageBase_Role_Missing_Should_Return_403()
    {
        var metadata = AdminMetadataResolver.ResolveByFunctionName(GetDashboardPage.EndpointName);
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/manage";
        context.Request.Headers[EasyAuthPrincipalParser.ClientPrincipalHeader] =
            EncodeEasyAuthPrincipal("other.staff@education.gov.uk", ["Some.Other.Role"]);

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, metadata: metadata);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeHttpAsync_Page_With_OperatorRole_Should_Allow_And_Set_User_And_UserContext()
    {
        var metadata = AdminMetadataResolver.ResolveByFunctionName(GetDashboardPage.EndpointName);
        var middleware = CreateMiddleware();
        var userContext = new UserContext();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/manage";
        context.Request.Headers[EasyAuthPrincipalParser.ClientPrincipalHeader] =
            EncodeEasyAuthPrincipal(
                "jane.estates@education.gov.uk",
                ["Edc.Admin.Operator"],
                oid: "oid-jane-456",
                tid: "tid-dfe-999");

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, userContext, metadata);

        nextCalled.Should().BeTrue();
        EasyAuthPrincipalParser.ResolveDisplayName(context.Request).Should().Be("jane.estates@education.gov.uk");
        userContext.IsAuthenticated.Should().BeTrue();
        userContext.UserId.Should().Be("oid-jane-456");
        userContext.Roles.Should().Contain("Edc.Admin.Operator");
    }

    [Fact]
    public async Task InvokeHttpAsync_Api_Without_Entra_Principal_Should_Return_401_ProblemJson_Even_With_FunctionKey_Header()
    {
        var metadata = AdminMetadataResolver.ResolveByFunctionName(PostVerifySchemaEndpoint.EndpointName);
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/schemas/verify";
        context.Request.Headers["x-functions-key"] = "host-key-value";
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, metadata: metadata);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.ContentType.Should().StartWith("application/problem+json");
    }

    [Fact]
    public async Task InvokeHttpAsync_Api_With_AutomationRole_Should_Pass()
    {
        var metadata = AdminMetadataResolver.ResolveByFunctionName(PostRunSchemaTestsEndpoint.EndpointName);
        var middleware = CreateMiddleware();
        var userContext = new UserContext();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/schemas/tests/run";
        context.Request.Headers[EasyAuthPrincipalParser.ClientPrincipalHeader] =
            EncodeEasyAuthPrincipal("github-cd-sp", ["Edc.Admin.Automation"], oid: "sp-oid-1", tid: "tid-dfe");

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, userContext, metadata);

        nextCalled.Should().BeTrue();
        userContext.IsAuthenticated.Should().BeTrue();
        userContext.HasRole("Edc.Admin.Automation").Should().BeTrue();
    }

    [Fact]
    public async Task InvokeHttpAsync_Api_With_Wrong_Role_Should_Return_403()
    {
        var metadata = AdminMetadataResolver.ResolveByFunctionName(GetAdminSchemasEndpoint.EndpointName);
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/schemas";
        context.Request.Headers[EasyAuthPrincipalParser.ClientPrincipalHeader] =
            EncodeEasyAuthPrincipal("jane.estates@education.gov.uk", ["Edc.Admin.Operator"]);
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, metadata: metadata);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.ContentType.Should().StartWith("application/problem+json");
    }

    [Fact]
    public async Task InvokeHttpAsync_Should_Resolve_RoleMappings_From_Configuration_And_Literal_Roles()
    {
        var customOptions = new AdminAuthOptions
        {
            Enabled = true,
            RoleMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AdminRoleKeys.Operator] = "group-guid-estates-admins,Edc.PreProd.Operator"
            }
        };

        var middleware = CreateMiddleware(customOptions);
        var pageMetadata = AdminMetadataResolver.ResolveByFunctionName(GetDashboardPage.EndpointName);
        var literalMetadata = EndpointAuthMetadata.RequireRole("Direct.Audit.Reader");

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/manage";
        context.Request.Headers[EasyAuthPrincipalParser.ClientPrincipalHeader] =
            EncodeEasyAuthPrincipal("ops@education.gov.uk", ["Edc.PreProd.Operator", "Direct.Audit.Reader"]);

        var pageAllowed = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            pageAllowed = true;
            return Task.CompletedTask;
        }, metadata: pageMetadata);

        var literalAllowed = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            literalAllowed = true;
            return Task.CompletedTask;
        }, metadata: literalMetadata);

        pageAllowed.Should().BeTrue();
        literalAllowed.Should().BeTrue();
    }
}
