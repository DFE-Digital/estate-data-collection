using AwesomeAssertions;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Tests.Common.Extensions;

public static class ResultAssertionExtensions
{
    public static T ShouldSucceed<T>(this Result<T> result)
    {
        result.IsSuccess.Should().BeTrue($"Expected success, but failed with {result.Error.Code}: {result.Error.Description}");
        return result.Value;
    }

    public static void ShouldSucceed(this Result result)
    {
        result.IsSuccess.Should().BeTrue($"Expected success, but failed with {result.Error.Code}: {result.Error.Description}");
    }

    public static Error ShouldFailWith<T>(this Result<T> result, string expectedErrorCode)
    {
        result.IsFailure.Should().BeTrue($"Expected failure '{expectedErrorCode}', but succeeded.");
        result.Error.Code.Should().Be(expectedErrorCode);
        return result.Error;
    }

    public static Error ShouldFailWith(this Result result, string expectedErrorCode)
    {
        result.IsFailure.Should().BeTrue($"Expected failure '{expectedErrorCode}', but succeeded.");
        result.Error.Code.Should().Be(expectedErrorCode);
        return result.Error;
    }
}
