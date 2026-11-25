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
        // Arrange
        var requestedConfig = new SchemaIdentifierConfig
        {
            Type = "LAEstab",
            JsonPointer = "establishment.laestab",
            Required = false
        };

        // Act
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NestedRequiredSchema,
            requestedConfig,
            ["school.urn", "urn"]);

        // Assert
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
        // Arrange
        var requestedConfig = new SchemaIdentifierConfig
        {
            Type = "UKPRN",
            JsonPointer = "/establishment/ukprn",
            Required = true
        };

        // Act
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NestedRequiredSchema,
            requestedConfig,
            ["school.urn", "urn"]);

        // Assert
        result.IsValid.Should().BeTrue();
        result.IsRequiredInSchema.Should().BeFalse();
        result.EffectiveConfig!.Required.Should().BeFalse();
    }

    [Fact]
    public void ValidateAndAlign_Should_Derive_Required_False_When_Schema_Allows_Null_Via_AnyOf()
    {
        // Arrange
        var requestedConfig = new SchemaIdentifierConfig
        {
            Type = "SchoolCode",
            JsonPointer = "/schoolCode"
        };

        // Act
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NullableIdentifierSchema,
            requestedConfig,
            ["school.urn", "urn"]);

        // Assert
        result.IsValid.Should().BeTrue();
        result.IsRequiredInSchema.Should().BeFalse();
        result.EffectiveConfig!.Required.Should().BeFalse();
    }

    [Fact]
    public void ValidateAndAlign_Should_Follow_Local_Refs_And_AllOf()
    {
        // Arrange
        var requestedConfig = new SchemaIdentifierConfig
        {
            Type = "UKPRN",
            JsonPointer = "/identity/ukprn"
        };

        // Act
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            RefAndAllOfSchema,
            requestedConfig,
            ["school.urn", "urn"]);

        // Assert
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
        // Arrange
        var requestedConfig = new SchemaIdentifierConfig { Type = "URN", JsonPointer = pointer };

        // Act
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NestedRequiredSchema,
            requestedConfig,
            ["school.urn", "urn"]);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateAndAlign_When_NullConfig_And_No_Default_Path_Matches_Should_Fail_When_Default_Required()
    {
        // Arrange — NestedRequiredSchema has no school.urn or urn path

        // Act
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NestedRequiredSchema,
            requestedConfig: null,
            ["school.urn", "urn"],
            requireDefaultIdentifier: true);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Identifier == "identifierConfig.jsonPointer");
    }

    [Fact]
    public void ValidateAndAlign_When_Type_Is_None_And_Pointer_Set_Should_Fail_Definition_Check()
    {
        // Arrange
        var requestedConfig = new SchemaIdentifierConfig
        {
            Type = "None",
            JsonPointer = "/establishment/laestab"
        };

        // Act
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NestedRequiredSchema,
            requestedConfig,
            ["school.urn", "urn"]);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Identifier == "identifierConfig.jsonPointer");
    }

    [Fact]
    public void ValidateAndAlign_When_Type_Is_None_Without_Pointer_Should_Disable_Without_Scanning_Schema()
    {
        // Arrange
        var requestedConfig = new SchemaIdentifierConfig { Type = "None", JsonPointer = null };

        // Act
        var result = IdentifierSchemaValidator.ValidateAndAlign(
            NestedRequiredSchema,
            requestedConfig,
            ["school.urn", "urn"]);

        // Assert
        result.IsValid.Should().BeTrue();
        result.IsEnabled.Should().BeFalse();
        result.EffectiveConfig!.IsDisabled.Should().BeTrue();
        result.EffectiveConfig.Required.Should().BeFalse();
    }
}
