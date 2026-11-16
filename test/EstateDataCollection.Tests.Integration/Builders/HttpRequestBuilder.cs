using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace EstateDataCollection.Tests.Integration.Builders;

public sealed class HttpRequestBuilder
{
    private string _body = string.Empty;
    private string _contentType = "application/json";
    private readonly Dictionary<string, StringValues> _query = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

    public static HttpRequest Empty() => new HttpRequestBuilder().Build();

    public static HttpRequest FromJson<T>(T dto) => new HttpRequestBuilder().WithJsonBody(dto).Build();

    public static HttpRequest FromRaw(string rawBody, string contentType = "application/json") =>
        new HttpRequestBuilder().WithRawBody(rawBody, contentType).Build();

    public HttpRequestBuilder WithJsonBody<T>(T dto)
    {
        _body = JsonSerializer.Serialize(dto);
        _contentType = "application/json";
        return this;
    }

    public HttpRequestBuilder WithRawBody(string body, string contentType = "application/json")
    {
        _body = body;
        _contentType = contentType;
        return this;
    }

    public HttpRequestBuilder WithContentType(string contentType)
    {
        _contentType = contentType;
        return this;
    }

    public HttpRequestBuilder WithQuery(string key, string value)
    {
        _query[key] = new StringValues(value);
        return this;
    }

    public HttpRequestBuilder WithHeader(string key, string value)
    {
        _headers[key] = value;
        return this;
    }

    public HttpRequestBuilder WithFileName(string fileName) => WithHeader("X-File-Name", fileName);

    public HttpRequestBuilder WithUrnHeaders(string pointer, bool required = true)
    {
        WithHeader("X-Urn-Pointer", pointer);
        WithHeader("X-Urn-Required", required ? "true" : "false");
        return this;
    }

    public HttpRequest Build()
    {
        var context = new DefaultHttpContext();
        if (_query.Count > 0)
        {
            context.Request.Query = new QueryCollection(_query);
        }

        foreach (var (key, value) in _headers)
        {
            context.Request.Headers[key] = new StringValues(value);
        }

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(_body));
        context.Request.Body = stream;
        context.Request.ContentLength = stream.Length;
        context.Request.ContentType = _contentType;
        return context.Request;
    }
}
