using System.Text;
using Microsoft.AspNetCore.Http;

namespace EstateDataCollection.AzureFunctions.Extensions;

public static class HttpRequestExtensions
{
    public static int? GetIntFromQuery(this HttpRequest request, string identifier, int? defaultValue = null)
    {
        return !request.Query.TryGetValue(identifier, out var raw) 
               || !int.TryParse(raw, out var result)
            ? defaultValue
            : result;
    }
    
    public static async Task<string> ReadRequestBodyAsync(this HttpRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        return body;
    }
}