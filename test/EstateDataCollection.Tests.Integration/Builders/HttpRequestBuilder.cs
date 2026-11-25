using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace EstateDataCollection.Tests.Integration.Builders;

public sealed class HttpRequestBuilder
{
    private string _body = string.Empty;
    private byte[]? _bodyBytes;
    private string _contentType = "application/json";
    private readonly Dictionary<string, StringValues> _query = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

    public static HttpRequest Empty()
    {
        return new HttpRequestBuilder().Build();
    }

    public static HttpRequest FromJson<T>(T dto)
    {
        return new HttpRequestBuilder().WithJsonBody(dto).Build();
    }

    public static HttpRequest FromRaw(string rawBody, string contentType = "application/json")
    {
        return new HttpRequestBuilder().WithRawBody(rawBody, contentType).Build();
    }

    public static async Task<HttpRequest> FromFormAsync(
        IReadOnlyDictionary<string, string?> fields,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var builder = new HttpRequestBuilder();
        await builder.WithFormBodyAsync(fields);
        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                builder.WithHeader(key, value);
            }
        }

        return builder.Build();
    }

    public HttpRequestBuilder WithJsonBody<T>(T dto)
    {
        _body = JsonSerializer.Serialize(dto);
        _bodyBytes = null;
        _contentType = "application/json";
        return this;
    }

    public HttpRequestBuilder WithRawBody(string body, string contentType = "application/json")
    {
        _body = body;
        _bodyBytes = null;
        _contentType = contentType;
        return this;
    }

    public async Task<HttpRequestBuilder> WithFormBodyAsync(IReadOnlyDictionary<string, string?> fields)
    {
        var content = new FormUrlEncodedContent(
            fields.Select(kv => new KeyValuePair<string?, string?>(kv.Key, kv.Value)));
        _bodyBytes = await content.ReadAsByteArrayAsync();
        _contentType = "application/x-www-form-urlencoded";
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

    public HttpRequestBuilder WithFileName(string fileName)
    {
        return WithHeader("X-File-Name", fileName);
    }

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

        var bytes = _bodyBytes ?? Encoding.UTF8.GetBytes(_body);
        var stream = new MemoryStream(bytes);
        context.Request.Body = stream;
        context.Request.ContentLength = stream.Length;
        context.Request.ContentType = _contentType;
        return context.Request;
    }
}
