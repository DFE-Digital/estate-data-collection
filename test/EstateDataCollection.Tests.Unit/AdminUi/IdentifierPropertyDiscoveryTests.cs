using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.AzureFunctions.Admin.Helpers;
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
        // Arrange — inputs provided by theory data

        // Act
        var derived = IdentifierPropertyDiscovery.DeriveTypeFromPointer(pointer);

        // Assert
        derived.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    public void DeriveTypeFromPointerOrNull_Should_Return_Null_When_Empty_Or_RootOnly(string? pointer)
    {
        // Arrange — inputs provided by theory data

        // Act
        var derived = IdentifierPropertyDiscovery.DeriveTypeFromPointerOrNull(pointer);

        // Assert
        derived.Should().BeNull();
    }

    [Fact]
    public void FromRawInputs_JsonSchema_Should_Return_Scalar_Leaf_Properties_And_Skip_Objects_Arrays_And_Booleans()
    {
        // Arrange
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

        // Act
        var props = IdentifierPropertyDiscovery.FromRawInputs("json", schemaJson, null);

        // Assert
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
        // Arrange
        var yaml = new OdcsContractYamlBuilder()
            .WithNestedSchoolUrn()
            .Build();

        // Act
        var props = IdentifierPropertyDiscovery.FromRawInputs("odcs", null, yaml);

        // Assert
        props.Should().ContainSingle(p =>
            p.Pointer == "/school/urn" &&
            p.Required &&
            p.DerivedType == "URN");
    }

    [Fact]
    public void WorkbenchFormParser_Should_Parse_Selected_Discovered_Property()
    {
        // Arrange
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = "/establishment/laestab",
            ["identifierPointer"] = "/establishment/laestab"
        });
        var errors = new List<ValidationError>();

        // Act
        var parsed = WorkbenchFormParser.ParseIdentifierConfig(form, errors);

        // Assert
        errors.Should().BeEmpty();
        parsed.Type.Should().Be("LAEstab");
        parsed.JsonPointer.Should().Be("/establishment/laestab");
    }

    [Fact]
    public void WorkbenchFormParser_Should_Parse_Custom_Pointer_And_Label()
    {
        // Arrange
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = IdentifierPropertyDiscovery.CustomOptionValue,
            ["identifierPointer"] = "/school/customCode",
            ["identifierLabel"] = "CustomCode"
        });
        var errors = new List<ValidationError>();

        // Act
        var parsed = WorkbenchFormParser.ParseIdentifierConfig(form, errors);

        // Assert
        errors.Should().BeEmpty();
        parsed.Type.Should().Be("CustomCode");
        parsed.JsonPointer.Should().Be("/school/customCode");
    }

    [Fact]
    public void WorkbenchFormParser_Should_Return_None_Config_When_IdentifierMode_Is_None()
    {
        // Arrange
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["identifierMode"] = "none",
            ["identifierProperty"] = "/establishment/laestab",
            ["identifierPointer"] = "/establishment/laestab"
        });
        var errors = new List<ValidationError>();

        // Act
        var parsed = WorkbenchFormParser.ParseIdentifierConfig(form, errors);

        // Assert
        errors.Should().BeEmpty();
        parsed.Type.Should().Be(SchemaIdentifierConfig.NoneType);
        parsed.JsonPointer.Should().BeNull();
    }

    [Fact]
    public void WorkbenchFormParser_Should_Require_Explicit_Property_Or_Pointer_When_Enabled()
    {
        // Arrange
        var emptyCustomForm = new FormCollection(new Dictionary<string, StringValues>
        {
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = IdentifierPropertyDiscovery.CustomOptionValue,
            ["identifierPointer"] = "   "
        });
        var errors = new List<ValidationError>();

        // Act
        var parsed = WorkbenchFormParser.ParseIdentifierConfig(emptyCustomForm, errors);

        // Assert
        errors.Should().Contain(e => e.PropertyPath == "identifierPointer");
        parsed.JsonPointer.Should().BeNull();
    }

    [Fact]
    public void ParsedWorkbenchInput_ResolveUiSelection_When_Config_Is_Null_Should_Not_Predetermine_Property()
    {
        // Arrange
        IReadOnlyList<SchemaIdentifierPropertyOption> available =
        [
            new("/school/urn", "urn", "integer", true, "URN"),
            new("/establishment/laestab", "laestab", "string", false, "LAEstab")
        ];

        // Act
        var selection = ParsedWorkbenchInput.ResolveUiSelection(null, available);

        // Assert
        selection.Should().Be(("enabled", IdentifierPropertyDiscovery.CustomOptionValue, string.Empty, string.Empty));
    }

    [Fact]
    public void ParsedWorkbenchInput_ResolveUiSelection_When_Pointer_Is_Blank_Should_Not_Predetermine_Property()
    {
        // Arrange
        IReadOnlyList<SchemaIdentifierPropertyOption> available =
        [
            new("/school/urn", "urn", "integer", true, "URN"),
            new("/establishment/laestab", "laestab", "string", false, "LAEstab")
        ];
        var config = new SchemaIdentifierConfig { Type = "URN", JsonPointer = null };

        // Act
        var selection = ParsedWorkbenchInput.ResolveUiSelection(config, available);

        // Assert
        selection.Should().Be(("enabled", IdentifierPropertyDiscovery.CustomOptionValue, string.Empty, string.Empty));
    }
}
