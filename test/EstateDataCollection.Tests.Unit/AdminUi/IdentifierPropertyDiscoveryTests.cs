using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.AzureFunctions.AdminUi.Helpers;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace EstateDataCollection.Tests.Unit.AdminUi;

public sealed class IdentifierPropertyDiscoveryTests
{
    [Theory]
    [InlineData("/urn", "URN")]
    [InlineData("/school/urn", "URN")]
    [InlineData("/establishment/ukprn", "UKPRN")]
    [InlineData("/establishment/laestab", "LAEstab")]
    [InlineData("/school/schoolCode", "schoolCode")]
    [InlineData(null, "URN")]
    public void DeriveTypeFromPointer_Should_Resolve_Canonical_And_Leaf_Labels(string? pointer, string expected)
    {
        IdentifierPropertyDiscovery.DeriveTypeFromPointer(pointer).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    public void DeriveTypeFromPointerOrNull_Should_Return_Null_When_Empty_Or_RootOnly(string? pointer)
    {
        IdentifierPropertyDiscovery.DeriveTypeFromPointerOrNull(pointer).Should().BeNull();
    }

    [Fact]
    public void FromRawInputs_JsonSchema_Should_Return_Scalar_Leaf_Properties_And_Skip_Objects_Arrays_And_Booleans()
    {
        const string schemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["establishment"],
          "properties": {
            "establishment": {
              "type": "object",
              "required": ["laestab"],
              "properties": {
                "laestab": { "type": "string" },
                "ukprn": { "type": "integer" }
              }
            },
            "is_active": { "type": "boolean" },
            "rooms": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": { "code": { "type": "string" } }
              }
            }
          }
        }
        """;

        var props = IdentifierPropertyDiscovery.FromRawInputs("json", schemaJson, null);

        props.Should().HaveCount(2);
        props.Should().Contain(p =>
            p.Pointer == "/establishment/laestab" &&
            p.Required &&
            p.DerivedType == "LAEstab");
        props.Should().Contain(p =>
            p.Pointer == "/establishment/ukprn" &&
            !p.Required &&
            p.DerivedType == "UKPRN");
    }

    [Fact]
    public void FromRawInputs_OdcsYaml_Should_Extract_Nested_Scalar_Properties()
    {
        var yaml = new OdcsContractYamlBuilder()
            .WithNestedSchoolUrn()
            .Build();

        var props = IdentifierPropertyDiscovery.FromRawInputs("odcs", null, yaml);

        props.Should().ContainSingle(p =>
            p.Pointer == "/school/urn" &&
            p.Required &&
            p.DerivedType == "URN");
    }

    [Fact]
    public void WorkbenchFormParser_Should_Handle_Property_Selection_Custom_Path_And_None_Mode()
    {
        var propertyForm = new FormCollection(new Dictionary<string, StringValues>
        {
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = "/establishment/laestab",
            ["identifierPointer"] = "/establishment/laestab"
        });

        var errors = new List<ValidationError>();
        var parsedProp = WorkbenchFormParser.ParseIdentifierConfig(propertyForm, errors);
        errors.Should().BeEmpty();
        parsedProp.Type.Should().Be("LAEstab");
        parsedProp.JsonPointer.Should().Be("/establishment/laestab");

        var customForm = new FormCollection(new Dictionary<string, StringValues>
        {
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = IdentifierPropertyDiscovery.CustomOptionValue,
            ["identifierPointer"] = "/school/customCode",
            ["identifierLabel"] = "CustomCode"
        });

        var parsedCustom = WorkbenchFormParser.ParseIdentifierConfig(customForm, errors);
        errors.Should().BeEmpty();
        parsedCustom.Type.Should().Be("CustomCode");
        parsedCustom.JsonPointer.Should().Be("/school/customCode");

        var noneForm = new FormCollection(new Dictionary<string, StringValues>
        {
            ["identifierMode"] = "none",
            ["identifierProperty"] = "/establishment/laestab",
            ["identifierPointer"] = "/establishment/laestab"
        });

        var parsedNone = WorkbenchFormParser.ParseIdentifierConfig(noneForm, errors);
        errors.Should().BeEmpty();
        parsedNone.Type.Should().Be(SchemaIdentifierConfig.NoneType);
        parsedNone.JsonPointer.Should().BeNull();
    }

    [Fact]
    public void WorkbenchFormParser_Should_Require_Explicit_Property_Or_Pointer_When_Enabled()
    {
        var emptyCustomForm = new FormCollection(new Dictionary<string, StringValues>
        {
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = IdentifierPropertyDiscovery.CustomOptionValue,
            ["identifierPointer"] = "   "
        });

        var errors = new List<ValidationError>();
        var parsed = WorkbenchFormParser.ParseIdentifierConfig(emptyCustomForm, errors);

        errors.Should().Contain(e => e.PropertyPath == "identifierPointer");
        parsed.JsonPointer.Should().BeNull();
    }

    [Fact]
    public void ParsedWorkbenchInput_ResolveUiSelection_Should_Never_Predetermine_Property_When_Pointer_Is_Empty()
    {
        IReadOnlyList<SchemaIdentifierPropertyOption> available =
        [
            new("/school/urn", "urn", "integer", true, "URN"),
            new("/establishment/laestab", "laestab", "string", false, "LAEstab")
        ];

        var fromNull = ParsedWorkbenchInput.ResolveUiSelection(null, available);
        fromNull.Should().Be(("enabled", IdentifierPropertyDiscovery.CustomOptionValue, string.Empty, string.Empty));

        var fromBlankPointer = ParsedWorkbenchInput.ResolveUiSelection(
            new SchemaIdentifierConfig { Type = "URN", JsonPointer = null },
            available);
        fromBlankPointer.Should().Be(("enabled", IdentifierPropertyDiscovery.CustomOptionValue, string.Empty, string.Empty));
    }
}
