using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.Application.UseCases.Schema.UpdateSchemaTests;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.AzureFunctions.AdminUi.Proxies;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Web.Models.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.AdminUi;

public sealed class AdminApiUseCaseProxyTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, string?, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : null;

            return responder(request, LastBody);
        }
    }

    private static IHttpClientFactory CreateFactory(StubHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://admin-api.test/api/")
        };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(AdminApiHttpClientNames.AdminApi).Returns(client);
        return factory;
    }

    private static HttpResponseMessage JsonEnvelopeResponse<T>(HttpStatusCode statusCode, ApiResponse<T> envelope)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(envelope, WebJson),
                Encoding.UTF8,
                "application/json")
        };
    }

    [Fact]
    public async Task HttpGetSchemasUseCaseProxy_Should_Send_Query_And_Deserialize_Catalogue()
    {
        var expected = new GetSchemasResult(
        [
            new SchemaCatalogueEntry("school-site", 1, [])
        ]);

        var handler = new StubHttpMessageHandler((_, _) =>
            JsonEnvelopeResponse(HttpStatusCode.OK, ApiResponse<GetSchemasResult>.Success(expected)));

        var proxy = new HttpGetSchemasUseCaseProxy(CreateFactory(handler));

        var result = (await proxy.ExecuteAsync(
            new GetSchemasRequest(SchemaName: "school-site", IncludeUnscheduled: true, IncludeActiveVersionFields: false),
            CancellationToken.None)).ShouldSucceed();

        result.Schemas.Should().ContainSingle(s => s.SchemaName == "school-site");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.ToString().Should().Be(
            "https://admin-api.test/api/schemas?includeUnscheduled=true&includeFields=false&schemaName=school-site");
    }

    [Fact]
    public async Task HttpGetSchemaDetailUseCaseProxy_Should_Map_404_ProblemDetails_To_NotFound_Error()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Schema.NotFound",
            Detail = "Schema 'missing' v1 does not exist."
        };

        var handler = new StubHttpMessageHandler((_, _) =>
            JsonEnvelopeResponse(HttpStatusCode.NotFound, ApiResponse<SchemaVersionDetail>.Failure(problem)));

        var proxy = new HttpGetSchemaDetailUseCaseProxy(CreateFactory(handler));

        var result = await proxy.ExecuteAsync(
            new GetSchemaDetailRequest("missing", 1, IncludeUnscheduled: true, IncludeRawJsonSchema: true),
            CancellationToken.None);

        var error = result.ShouldFailWith("Schema.NotFound");
        error.Type.Should().Be(ErrorType.NotFound);
        error.Description.Should().Contain("does not exist");
    }

    [Fact]
    public async Task HttpVerifySchemaUseCaseProxy_Should_Forward_IdentifierConfig_And_Map_422_Errors()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Schema.VerificationFailed",
            Detail = "Candidate schema failed pre-flight tests."
        };
        problem.Extensions["errors"] = new Dictionary<string, string[]>
        {
            ["schema"] = ["Syntax error on line 2", "Unknown keyword"]
        };

        var handler = new StubHttpMessageHandler((_, _) =>
            JsonEnvelopeResponse(
                HttpStatusCode.UnprocessableEntity,
                ApiResponse<VerifySchemaResult>.Failure(problem)));

        var proxy = new HttpVerifySchemaUseCaseProxy(CreateFactory(handler));

        var result = await proxy.ExecuteAsync(
            new VerifySchemaRequest(
                "{}",
                [],
                [],
                OdcsYaml: null,
                IdentifierConfig: new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab" }),
            CancellationToken.None);

        var error = result.ShouldFailWith("Schema.VerificationFailed");
        error.Type.Should().Be(ErrorType.Validation);
        error.ValidationErrors.Should().HaveCount(2);
        error.ValidationErrors[0].PropertyPath.Should().Be("schema");
        error.ValidationErrors[0].Message.Should().Be("Syntax error on line 2");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should().Be("https://admin-api.test/api/schemas/verify");
        handler.LastBody.Should().Contain("\"identifierConfig\"");
        handler.LastBody.Should().Contain("LAEstab");
    }

    [Fact]
    public async Task HttpSubmitSchemaUseCaseProxy_Should_Map_409_Conflict_And_Deserialize_Success()
    {
        var expected = new SubmitSchemaResult(
            "school-site",
            1,
            SchemaLifecycleState.Validated.ToNode(),
            "sha256",
            null,
            null,
            new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true });

        var callCount = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return JsonEnvelopeResponse(HttpStatusCode.Created, ApiResponse<SubmitSchemaResult>.Success(expected));
            }

            return JsonEnvelopeResponse(
                HttpStatusCode.Conflict,
                ApiResponse<SubmitSchemaResult>.Failure(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Schema.AlreadyExists",
                    Detail = "Already exists"
                }));
        });

        var proxy = new HttpSubmitSchemaUseCaseProxy(CreateFactory(handler));
        var req = new SubmitSchemaRequest("school-site", 1, "{\"type\":\"object\"}");

        var first = (await proxy.ExecuteAsync(req, CancellationToken.None)).ShouldSucceed();
        first.State.Name.Should().Be("Validated");

        var second = await proxy.ExecuteAsync(req, CancellationToken.None);
        var conflictError = second.ShouldFailWith("Schema.AlreadyExists");
        conflictError.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task HttpScheduleSchemaUseCaseProxy_Should_Send_Patch_And_Handle_Network_Exception()
    {
        var expected = new ScheduleSchemaResult(
            "school-site",
            1,
            SchemaLifecycleState.Active.ToNode(),
            DateTimeOffset.UtcNow,
            null,
            "sha256");

        var handler = new StubHttpMessageHandler((_, _) =>
            JsonEnvelopeResponse(HttpStatusCode.OK, ApiResponse<ScheduleSchemaResult>.Success(expected)));

        var proxy = new HttpScheduleSchemaUseCaseProxy(CreateFactory(handler));

        var ok = (await proxy.ExecuteAsync(
            new ScheduleSchemaRequest("school-site", 1, LifecycleAction: "Activate"),
            CancellationToken.None)).ShouldSucceed();

        ok.State.Name.Should().Be("Active");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        handler.LastRequest.RequestUri!.ToString().Should().Be(
            "https://admin-api.test/api/schemas/school-site/versions/1/schedule");

        var failingHandler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("Connection refused"));
        var failingProxy = new HttpScheduleSchemaUseCaseProxy(CreateFactory(failingHandler));

        var failed = await failingProxy.ExecuteAsync(
            new ScheduleSchemaRequest("school-site", 1, LifecycleAction: "Activate"),
            CancellationToken.None);

        failed.ShouldFailWith("AdminApi.Unavailable");
    }

    [Fact]
    public async Task HttpRunSchemaTestsUseCaseProxy_Should_Post_Request_And_Deserialize_Results()
    {
        var expected = new RunSchemaTestsResult(
            ExecutedAtUtc: DateTimeOffset.UtcNow,
            AppVersion: "2026.11.1",
            TotalSchemasChecked: 1,
            PassedCount: 1,
            FailedCount: 0,
            Results:
            [
                new SchemaTestSuiteItemResult("school-site", 1, "Active", true, true, 1, 1, 2, [], "URN", "/urn")
            ]);

        var handler = new StubHttpMessageHandler((_, _) =>
            JsonEnvelopeResponse(HttpStatusCode.OK, ApiResponse<RunSchemaTestsResult>.Success(expected)));

        var proxy = new HttpRunSchemaTestsUseCaseProxy(CreateFactory(handler));

        var res = (await proxy.ExecuteAsync(new RunSchemaTestsRequest("school-site", 1), CancellationToken.None)).ShouldSucceed();

        res.PassedCount.Should().Be(1);
        res.Results.Should().ContainSingle(r => r.SchemaName == "school-site" && r.BusinessRuleCount == 2);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should().Be("https://admin-api.test/api/schemas/tests/run");
    }

    [Fact]
    public async Task HttpUpdateSchemaTestsUseCaseProxy_Should_Put_Fixtures_And_Map_Validation_Errors()
    {
        var expected = new UpdateSchemaTestsResult(
            SchemaName: "school-site",
            Version: 1,
            ValidFixtureCount: 1,
            InvalidFixtureCount: 1,
            BusinessRuleCount: 2,
            ExecutedAtUtc: DateTimeOffset.UtcNow,
            AppVersion: "2026.11.1");

        var callCount = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return JsonEnvelopeResponse(HttpStatusCode.OK, ApiResponse<UpdateSchemaTestsResult>.Success(expected));
            }

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Schema.VerificationFailed",
                Detail = "Candidate schema failed pre-flight tests."
            };
            problem.Extensions["errors"] = new Dictionary<string, string[]>
            {
                ["SampleValidFixtures:0"] = ["SampleValidFixtures:0 violated quality rules: positive-capacity"]
            };
            return JsonEnvelopeResponse(HttpStatusCode.UnprocessableEntity, ApiResponse<UpdateSchemaTestsResult>.Failure(problem));
        });

        var proxy = new HttpUpdateSchemaTestsUseCaseProxy(CreateFactory(handler));
        var req = new UpdateSchemaTestsRequest("school-site", 1, ["""{"urn":123456}"""], ["""{"urn":"bad"}"""]);

        var ok = (await proxy.ExecuteAsync(req, CancellationToken.None)).ShouldSucceed();
        ok.ValidFixtureCount.Should().Be(1);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Put);
        handler.LastRequest.RequestUri!.ToString().Should().Be("https://admin-api.test/api/schemas/school-site/versions/1/tests");
        handler.LastBody.Should().Contain("\"validFixtures\"");

        var fail = await proxy.ExecuteAsync(req, CancellationToken.None);
        var err = fail.ShouldFailWith("Schema.VerificationFailed");
        err.Type.Should().Be(ErrorType.Validation);
        err.ValidationErrors.Should().ContainSingle(e => e.PropertyPath == "SampleValidFixtures:0");
    }
}
