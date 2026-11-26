using System.Security.Claims;
using AwesomeAssertions;
using EstateDataCollection.AzureFunctions.Public.Middleware;
using EstateDataCollection.AzureFunctions.Public.Options;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.AzureFunctions.Public.Security;
using EstateDataCollection.Common.Security;
using EstateDataCollection.Common.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using msOptions = Microsoft.Extensions.Options.Options;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Public;

public sealed class ActorContextMiddlewareTests
{
    private static readonly FunctionAuthMetadataResolver PublicMetadataResolver =
        new(typeof(GetSchemasEndpoint).Assembly);

    private static ActorContextMiddleware CreateMiddleware(PublicAuthOptions? options = null)
    {
        return new ActorContextMiddleware(
            msOptions.Create(options ?? new PublicAuthOptions { Enabled = true }),
            NullLogger<ActorContextMiddleware>.Instance,
            PublicMetadataResolver);
    }

    [Fact]
    public async Task InvokeHttpAsync_When_Disabled_Should_Pass_Through_And_Still_Resolve_FaUApi_Actor()
    {
        var middleware = CreateMiddleware(new PublicAuthOptions { Enabled = false });
        var actorContext = new ActorContext();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/submissions/cdc_survey";
        context.Request.Headers["Ocp-Apim-Subscription-Key"] = "super-secret-raw-subscription-key";
        context.Request.Headers["X-FaUAPI-Subscription-Id"] = "sub-acme-01";
        context.Request.Headers["X-FaUAPI-Subscription-Name"] = "Acme Estate Surveyors";

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, actorContext);

        nextCalled.Should().BeTrue();
        actorContext.IsAuthenticated.Should().BeTrue();
        actorContext.ActorId.Should().Be("sub-acme-01");
        actorContext.ActorName.Should().Be("Acme Estate Surveyors");
        actorContext.SubscriptionId.Should().Be("sub-acme-01");
        actorContext.SubscriptionName.Should().Be("Acme Estate Surveyors");
        actorContext.AuthenticationType.Should().Be("FaUApiSubscriptionKey");
        actorContext.AuditActorDisplay.Should().Be("Acme Estate Surveyors (sub-acme-01)");
        actorContext.Metadata.Should().ContainKey("keyFingerprint");
        actorContext.Metadata["keyFingerprint"].Should().StartWith("fauapi-key:");
        actorContext.Metadata.Values.Should().NotContain(v => v.Contains("super-secret-raw-subscription-key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvokeHttpAsync_When_Enabled_With_Raw_SubscriptionKey_Only_Should_Use_NonReversible_Fingerprint()
    {
        const string rawKey = "fauapi-raw-secret-key-999";
        var fingerprint = FaUApiActorResolver.ComputeKeyFingerprint(rawKey);

        var middleware = CreateMiddleware(new PublicAuthOptions
        {
            Enabled = true,
            SubscriptionKeySurveyorMap = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [fingerprint] = "DfE Pilot Surveyor A"
            }
        });

        var actorContext = new ActorContext();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/schemas";
        context.Request.Headers["Ocp-Apim-Subscription-Key"] = rawKey;

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, actorContext);

        nextCalled.Should().BeTrue();
        actorContext.IsAuthenticated.Should().BeTrue();
        actorContext.ActorId.Should().Be(fingerprint);
        actorContext.ActorName.Should().Be("DfE Pilot Surveyor A");
        actorContext.AuditActorDisplay.Should().Be($"DfE Pilot Surveyor A ({fingerprint})");
        actorContext.AuditActorDisplay.Should().NotContain(rawKey);
    }

    [Theory]
    [InlineData(GetVersionAboutEndpoint.EndpointName)]
    [InlineData(GetOpenApiJsonEndpoint.EndpointName)]
    [InlineData(GetOpenApiYamlEndpoint.EndpointName)]
    [InlineData(GetScalarDocsEndpoint.EndpointName)]
    public async Task InvokeHttpAsync_AllowAnonymous_Attribute_Should_Bypass_Actor_Enforcement(string endpointName)
    {
        var metadata = PublicMetadataResolver.ResolveByFunctionName(endpointName);
        metadata.AllowAnonymous.Should().BeTrue();

        var middleware = CreateMiddleware(new PublicAuthOptions { Enabled = true, RequireActorOnAuthenticatedRoutes = true });
        var actorContext = new ActorContext();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, actorContext, metadata);

        nextCalled.Should().BeTrue();
        actorContext.IsAuthenticated.Should().BeFalse();
        actorContext.AuthenticationType.Should().Be("Anonymous");
    }

    [Theory]
    [InlineData(GetSchemasEndpoint.EndpointName)]
    [InlineData(PostCreateSubmissionEndpoint.EndpointName)]
    public async Task InvokeHttpAsync_Protected_Endpoints_Without_Credentials_When_Enabled_Should_Return_401_ProblemJson(string endpointName)
    {
        var metadata = PublicMetadataResolver.ResolveByFunctionName(endpointName);
        metadata.AllowAnonymous.Should().BeFalse();

        var middleware = CreateMiddleware(new PublicAuthOptions { Enabled = true, RequireActorOnAuthenticatedRoutes = true });
        var actorContext = new ActorContext();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/protected";
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, actorContext, metadata);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.ContentType.Should().StartWith("application/problem+json");
        actorContext.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeHttpAsync_With_Future_OAuth2_ClaimsPrincipal_And_SubscriptionKey_Should_Combine_Context()
    {
        var middleware = CreateMiddleware(new PublicAuthOptions { Enabled = true });
        var actorContext = new ActorContext();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/submissions/cdc_survey";
        context.Request.Headers["X-FaUAPI-Subscription-Id"] = "sub-vendor-42";
        context.Request.Headers["X-FaUAPI-Subscription-Name"] = "Vendor Surveys Ltd";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("azp", "client-app-9988"),
            new Claim("name", "Vendor Ingestion App")
        ], authenticationType: "Bearer"));

        var nextCalled = false;
        await middleware.InvokeHttpAsync(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, actorContext);

        nextCalled.Should().BeTrue();
        actorContext.IsAuthenticated.Should().BeTrue();
        actorContext.AuthenticationType.Should().Be("FaUApiOAuth2+SubscriptionKey");
        actorContext.ActorId.Should().Be("client-app-9988");
        actorContext.ActorName.Should().Be("Vendor Surveys Ltd");
        actorContext.SubscriptionId.Should().Be("sub-vendor-42");
        actorContext.Metadata["oauthClientId"].Should().Be("client-app-9988");
    }
}
