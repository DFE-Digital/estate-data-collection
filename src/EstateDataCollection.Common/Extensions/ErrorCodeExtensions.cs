using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using EstateDataCollection.Common.Results;
using static System.StringComparer;

namespace EstateDataCollection.Common.Extensions;

public static class ErrorCodesExtensions
{
    private sealed record ErrorMeta(string Name, string Description);
    private static readonly ConcurrentDictionary<ErrorCodes, ErrorMeta> Cache = new();

    /// <summary>
    /// Stable dotted identifier, e.g. "Schema.NotFound".
    /// </summary>
    /// <param name="code"><see cref="ErrorCodes"/> code wanting to get name of</param>
    public static string GetName(this ErrorCodes code)
    {
        return GetMeta(code).Name;
    }

    /// <summary>
    /// Gets the template string for the error code.
    /// </summary>
    /// <param name="code"><see cref="ErrorCodes"/> code wanting to get template of</param>
    public static string GetTemplate(this ErrorCodes code)
    {
        return GetMeta(code).Description;
    }

    /// <summary>
    /// Get description of error. This is the template which has been parsed
    /// </summary>
    /// <param name="code"><see cref="ErrorCodes"/> code wanting to get description of</param>
    /// <param name="args">Dictionary or anonymous object, e.g. new { SchemaName = "x", Version = 2 }</param>
    public static string GetMessage(this ErrorCodes code, object? args = null)
    {
        var template = GetMeta(code).Description;
        return args is null 
            ? template 
            : template.FormatWith(ToDictionary(args));
    }

    private static ErrorMeta GetMeta(ErrorCodes code)
    {
        return Cache.GetOrAdd(code, static c =>
        {
            var display = typeof(ErrorCodes)
                .GetField(c.ToString())
                ?.GetCustomAttribute<DisplayAttribute>();
            
            return new ErrorMeta(
                display?.GetName() ?? c.ToString(),
                display?.GetDescription() ?? string.Empty);
        });
    }

    private static Dictionary<string, object?> ToDictionary(object args)
    {
        return args switch
        {
            IReadOnlyDictionary<string, object?> ro => new Dictionary<string, object?>(ro, OrdinalIgnoreCase),
            IDictionary<string, object?> d => new Dictionary<string, object?>(d, OrdinalIgnoreCase),
            
            _ => args.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(p => p.Name, p => p.GetValue(args), OrdinalIgnoreCase)
        };
    }
}