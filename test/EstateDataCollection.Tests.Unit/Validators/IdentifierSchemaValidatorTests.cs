using AwesomeAssertions;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Domain.Schema;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Validators;

public sealed class IdentifierSchemaValidatorTests
{
    private const string NestedRequiredSchema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["establishment"],
      "properties": {
        "establishment": {
          "type": "object",
          "required": ["laestab", "metadata"],
          "properties": {
            "laestab": { "type": "string", "pattern": "^[0-9]{3}-[0-9]{4}$" },
            "ukprn": { "type": "integer" },
            "metadata": { "type": "object", "properties": { "note": { "type": "string" } } },
            "tags": { "type": "array", "items": { "type": "string" } },
            "isOpen": { "type": "boolean" }
          }
        }
      }
    }
    """;

    private const string RefAndAllOfSchema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "$defs": {
        "SchoolIdentity": {
          "type": "object",
          "required": ["ukprn"],
          "properties": {
            "ukprn": { "type": ["string", "integer"] }
          }
        }
      },
      "allOf": [
        {
          "type": "object",
          "required": ["identity"],
          "properties": {
            "identity": { "$ref": "#/$defs/SchoolIdentity" }
          }
        }
      ]
    }
    """;

    private const string NullableIdentifierSchema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["schoolCode"],
      "properties": {
        "schoolCode": {
          "anyOf": [
            { "type": "string" },
            { "type": "null" }
          ]
        }
      }
    }
    """;

    [Fact]
    public void ValidateAndAlign_Should_Validate_Required_Nested_Custom_Identifier_And_Derive_Required_True()
    {
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NestedRequiredSchema,
            new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "establishment.laestab", Required = false },
            ["school.urn", "urn"]);

        result.IsValid.Should().BeTrue();
        result.IsEnabled.Should().BeTrue();
        result.IsRequiredInSchema.Should().BeTrue();
        result.ResolvedPaths.Should().ContainSingle().Which.Should().Be("/establishment/laestab");
        result.EffectiveConfig.Should().NotBeNull();
        result.EffectiveConfig!.Type.Should().Be("LAEstab");
        result.EffectiveConfig.JsonPointer.Should().Be("/establishment/laestab");
        result.EffectiveConfig.Required.Should().BeTrue();
    }

    [Fact]
    public void ValidateAndAlign_Should_Derive_Required_False_When_Leaf_Is_Not_In_Required_Array()
    {
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NestedRequiredSchema,
            new SchemaIdentifierConfig { Type = "UKPRN", JsonPointer = "/establishment/ukprn", Required = true },
            ["school.urn", "urn"]);

        result.IsValid.Should().BeTrue();
        result.IsRequiredInSchema.Should().BeFalse();
        result.EffectiveConfig!.Required.Should().BeFalse();
    }

    [Fact]
    public void ValidateAndAlign_Should_Derive_Required_False_When_Schema_Allows_Null_Via_AnyOf()
    {
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NullableIdentifierSchema,
            new SchemaIdentifierConfig { Type = "SchoolCode", JsonPointer = "/schoolCode" },
            ["school.urn", "urn"]);

        result.IsValid.Should().BeTrue();
        result.IsRequiredInSchema.Should().BeFalse();
        result.EffectiveConfig!.Required.Should().BeFalse();
    }

    [Fact]
    public void ValidateAndAlign_Should_Follow_Local_Refs_And_AllOf()
    {
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            RefAndAllOfSchema,
            new SchemaIdentifierConfig { Type = "UKPRN", JsonPointer = "/identity/ukprn" },
            ["school.urn", "urn"]);

        result.IsValid.Should().BeTrue();
        result.IsRequiredInSchema.Should().BeTrue();
        result.EffectiveConfig!.Required.Should().BeTrue();
    }

    [Theory]
    [InlineData("/establishment/missing", "not declared in the JSON Schema")]
    [InlineData("/establishment/metadata", "incompatible type")]
    [InlineData("/establishment/tags", "incompatible type")]
    [InlineData("/establishment/isOpen", "incompatible type")]
    public void ValidateAndAlign_Should_Reject_Missing_Or_NonScalar_Pointers(string pointer, string expectedFragment)
    {
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NestedRequiredSchema,
            new SchemaIdentifierConfig { Type = "URN", JsonPointer = pointer },
            ["school.urn", "urn"]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateAndAlign_When_NullConfig_And_No_Default_Path_Matches_Should_Fail_When_Default_Required()
    {
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NestedRequiredSchema,
            requestedConfig: null,
            ["school.urn", "urn"],
            requireDefaultIdentifier: true);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Identifier == "identifierConfig.jsonPointer");
    }

    [Fact]
    public void ValidateAndAlign_When_Type_Is_None_And_Pointer_Set_Should_Fail_Definition_Check()
    {
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NestedRequiredSchema,
            new SchemaIdentifierConfig { Type = "None", JsonPointer = "/establishment/laestab" },
            ["school.urn", "urn"]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Identifier == "identifierConfig.jsonPointer");
    }

    [Fact]
    public void ValidateAndAlign_When_Type_Is_None_Without_Pointer_Should_Disable_Without_Scanning_Schema()
    {
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NestedRequiredSchema,
            new SchemaIdentifierConfig { Type = "None", JsonPointer = null },
            ["school.urn", "urn"]);

        result.IsValid.Should().BeTrue();
        result.IsEnabled.Should().BeFalse();
        result.EffectiveConfig!.IsDisabled.Should().BeTrue();
        result.EffectiveConfig.Required.Should().BeFalse();
    }
}
