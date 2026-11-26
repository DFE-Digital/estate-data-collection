using System.Text;
using System.Xml.Linq;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace EstateDataCollection.AzureFunctions.Public.OpenApi;

/// <summary>
/// Populates OpenAPI schema property descriptions from `param name="..."` XML comments
/// declared on C# positional records when Swashbuckle's built-in XML filter only inspects `P:` property members.
/// </summary>
public sealed class RecordParameterXmlDocSchemaFilter : ISchemaFilter
{
    private readonly Dictionary<string, Dictionary<string, string>> _typeParamDescriptions;

    public RecordParameterXmlDocSchemaFilter(IEnumerable<string> xmlDocumentationPaths)
    {
        _typeParamDescriptions = BuildLookup(xmlDocumentationPaths);
    }

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema concrete ||
            concrete.Properties is null ||
            concrete.Properties.Count == 0)
        {
            return;
        }

        var typeKey = FormatTypeMemberName(context.Type);
        if (typeKey is null || !_typeParamDescriptions.TryGetValue(typeKey, out var paramDescriptions))
        {
            return;
        }

        foreach (var (propertyName, propertySchema) in concrete.Properties)
        {
            if (propertySchema is not OpenApiSchema concreteProperty ||
                !string.IsNullOrWhiteSpace(concreteProperty.Description))
            {
                continue;
            }

            if (paramDescriptions.TryGetValue(propertyName, out var description))
            {
                concreteProperty.Description = description;
            }
        }
    }

    private static Dictionary<string, Dictionary<string, string>> BuildLookup(IEnumerable<string> xmlPaths)
    {
        var lookup = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var xmlPath in xmlPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(xmlPath))
            {
                continue;
            }

            XDocument doc;
            try
            {
                doc = XDocument.Load(xmlPath);
            }
            catch
            {
                continue;
            }

            var members = doc.Root?.Element("members")?.Elements("member");
            if (members is null)
            {
                continue;
            }

            foreach (var member in members)
            {
                var memberName = member.Attribute("name")?.Value;
                if (string.IsNullOrWhiteSpace(memberName))
                {
                    continue;
                }

                string? typeKey = null;
                if (memberName.StartsWith("T:", StringComparison.Ordinal))
                {
                    typeKey = memberName[2..];
                }
                else if (memberName.StartsWith("M:", StringComparison.Ordinal))
                {
                    var ctorIndex = memberName.IndexOf(".#ctor", StringComparison.Ordinal);
                    if (ctorIndex > 2)
                    {
                        typeKey = memberName[2..ctorIndex];
                    }
                }

                if (typeKey is null)
                {
                    continue;
                }

                foreach (var paramElement in member.Elements("param"))
                {
                    var paramName = paramElement.Attribute("name")?.Value;
                    if (string.IsNullOrWhiteSpace(paramName))
                    {
                        continue;
                    }

                    var text = ExtractFormattedText(paramElement);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    if (!lookup.TryGetValue(typeKey, out var paramMap))
                    {
                        paramMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        lookup[typeKey] = paramMap;
                    }

                    paramMap.TryAdd(paramName, text);
                }
            }
        }

        return lookup;
    }

    private static string? FormatTypeMemberName(Type type)
    {
        var baseType = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        return baseType.FullName?.Replace('+', '.');
    }

    private static string ExtractFormattedText(XElement element)
    {
        var sb = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText textNode:
                    sb.Append(textNode.Value);
                    break;
                case XElement child when child.Name.LocalName == "see":
                    var langword = child.Attribute("langword")?.Value;
                    var cref = child.Attribute("cref")?.Value;
                    if (!string.IsNullOrWhiteSpace(langword))
                    {
                        sb.Append('`').Append(langword).Append('`');
                    }
                    else if (!string.IsNullOrWhiteSpace(cref))
                    {
                        var shortName = cref.Split('.').Last().Split(':').Last();
                        sb.Append('`').Append(shortName).Append('`');
                    }
                    break;
                case XElement child when child.Name.LocalName is "c" or "paramref" or "typeparamref":
                    var refName = child.Attribute("name")?.Value;
                    sb.Append('`').Append(!string.IsNullOrWhiteSpace(refName) ? refName : child.Value).Append('`');
                    break;
                case XElement child:
                    sb.Append(ExtractFormattedText(child));
                    break;
            }
        }

        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
