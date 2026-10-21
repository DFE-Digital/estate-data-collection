using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

namespace EstateDataCollection.Common.Extensions;

public static partial class StringExtensions
{
    public static string FormatWith(this string template, object values)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template ?? string.Empty;
        }

        // If it's already a dictionary (e.g. from ErrorCodeExtensions)
        if (values is IDictionary dict)
        {
            return FormatWithNodeWrapperRegex().Replace(template, m =>
            {
                var key = m.Groups[1].Value;
                foreach (DictionaryEntry entry in dict)
                {
                    if (string.Equals(entry.Key.ToString(), key, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.Value?.ToString() ?? string.Empty;
                    }
                }
                return m.Value;
            });
        }

        // If it's an object/POCO/anonymous type, search properties case-insensitively
        var properties = values.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        return FormatWithNodeWrapperRegex().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            var prop = properties.FirstOrDefault(p => 
                string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));

            return prop?.GetValue(values)?.ToString() ?? m.Value;
        });
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex FormatWithNodeWrapperRegex();
}