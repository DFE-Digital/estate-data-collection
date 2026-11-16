using AwesomeAssertions;
using EstateDataCollection.Common.Web.Models.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EstateDataCollection.Tests.Integration.Extensions;

public static class ActionResultAssertionExtensions
{
    public static T ShouldBeApiSuccess<T>(
        this IActionResult actionResult,
        int expectedStatusCode = StatusCodes.Status200OK)
    {
        var objectResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(expectedStatusCode);

        var response = objectResult.Value.Should().BeOfType<ApiResponse<T>>().Subject;
        response.Successful.Should().BeTrue();
        response.Data.Should().NotBeNull();
        return response.Data!;
    }

    public static ProblemDetails ShouldBeApiFailure<T>(
        this IActionResult actionResult,
        int expectedStatusCode,
        string? expectedErrorCode = null)
    {
        var objectResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(expectedStatusCode);

        var response = objectResult.Value.Should().BeOfType<ApiResponse<T>>().Subject;
        response.Successful.Should().BeFalse();
        response.Problem.Should().NotBeNull();

        if (!string.IsNullOrWhiteSpace(expectedErrorCode))
        {
            response.Problem!.Title.Should().Be(expectedErrorCode);
        }

        return response.Problem!;
    }

    public static void ShouldHaveStatusCode(this IActionResult actionResult, int expectedStatusCode)
    {
        var objectResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(expectedStatusCode);
    }
}
