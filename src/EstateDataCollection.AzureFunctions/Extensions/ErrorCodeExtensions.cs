using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Results;
using Microsoft.AspNetCore.Mvc;

namespace EstateDataCollection.AzureFunctions.Extensions;

public static class ErrorCodeExtensions
{
    public static ProblemDetails ToProblem(this ErrorCodes code, int status, params object?[] args)
    {
        return new ProblemDetails
        {
            Status = status,
            Type = null, // to declare eg. $"https://api.example.com/errors/{code}"
            Title = code.ToString(),
            Detail = code.GetMessage(args),
            Extensions = { ["code"] = (int)code }
        };
    }
}