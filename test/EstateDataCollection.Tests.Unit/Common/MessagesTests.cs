using System.ComponentModel.DataAnnotations;
using System.Reflection;
using AwesomeAssertions;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Common;

public sealed class MessagesTests
{
    [Fact]
    public void All_Message_Classes_Should_Have_NonEmpty_Constants()
    {
        // Arrange
        Type[] messageTypes =
        [
            typeof(ErrorMessages),
            typeof(DomainMessages),
            typeof(SchemaValidationMessages),
            typeof(ConsistencyAnalyserMessages),
            typeof(WorkflowMessages),
            typeof(SubmissionMessages),
            typeof(AdminUiMessages)
        ];

        // Act
        var constantValues = messageTypes
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (Type: f.DeclaringType!.Name, Name: f.Name, Value: f.GetRawConstantValue() as string))
            .ToList();

        // Assert
        constantValues.Should().NotBeEmpty();
        foreach (var entry in constantValues)
        {
            entry.Value.Should().NotBeNullOrWhiteSpace($"{entry.Type}.{entry.Name} should be non-empty");
        }
    }

    [Fact]
    public void ErrorCodes_Members_Should_Declare_Display_Names_And_Descriptions()
    {
        // Arrange
        var errorCodeMembers = Enum.GetValues<ErrorCodes>()
            .Where(c => c != ErrorCodes.None)
            .ToList();

        // Act
        var displays = errorCodeMembers
            .Select(code =>
            {
                var field = typeof(ErrorCodes).GetField(code.ToString())!;
                return (Code: code, Display: field.GetCustomAttribute<DisplayAttribute>());
            })
            .ToList();

        // Assert
        displays.Should().AllSatisfy(x =>
        {
            x.Display.Should().NotBeNull($"{x.Code} must declare [Display]");
            x.Display!.Name.Should().NotBeNullOrWhiteSpace($"{x.Code} must declare Display.Name");
            x.Display.Description.Should().NotBeNullOrWhiteSpace($"{x.Code} must declare Display.Description");
        });
    }

    [Fact]
    public void ErrorMessages_RequestBody_Formatters_Should_Format_Mb_Limits()
    {
        // Arrange
        const int maxSizeMb = 5;
        const long smallBytes = 512;
        const long multiMbBytes = 4L * 1024 * 1024;

        // Act
        var limitMessage = ErrorMessages.RequestBodyExceedsLimitMb(maxSizeMb);
        var clampedSmallMessage = ErrorMessages.RequestBodyTooLarge(smallBytes);
        var multiMbMessage = ErrorMessages.RequestBodyTooLarge(multiMbBytes);

        // Assert
        limitMessage.Should().Be("The request payload exceeds the configured maximum limit of 5 MB.");
        clampedSmallMessage.Should().Be("The request payload exceeds the configured maximum limit of 1 MB.");
        multiMbMessage.Should().Be("The request payload exceeds the configured maximum limit of 4 MB.");
    }

    [Fact]
    public void DomainMessages_Formatters_Should_Preserve_Expected_Wording()
    {
        // Arrange
        const string typeName = "URN";
        const string pointer = "/school/urn";
        const string badType = "bad type";

        // Act
        var invalidType = DomainMessages.IdentifierTypeInvalidChars(badType);
        var emptyId = DomainMessages.IdentifierAtPathCannotBeEmpty(typeName, pointer);
        var unsafeChars = DomainMessages.IdentifierUnsafePartitionChars(typeName, "123/45");

        // Assert
        invalidType.Should().Be("Identifier Type 'bad type' must not contain whitespace, slashes, or '..'.");
        emptyId.Should().Be("Identifier 'URN' at '/school/urn' cannot be empty.");
        unsafeChars.Should().Be("Identifier 'URN' value '123/45' contains characters not permitted for storage partitioning ('/', '\\', or '..').");
    }

    [Fact]
    public void SchemaValidationMessages_FixtureCheckSummary_Should_Include_Triggers_Only_When_Positive()
    {
        // Arrange
        const string prefix = "Schema valid";

        // Act
        var withoutTriggers = SchemaValidationMessages.FixtureCheckSummary(prefix, 2, 1, 0);
        var withTriggers = SchemaValidationMessages.FixtureCheckSummary(prefix, 2, 1, 1);

        // Assert
        withoutTriggers.Should().Be("Schema valid; checked 2 valid and 1 invalid fixture(s)");
        withTriggers.Should().Be("Schema valid; checked 2 valid and 1 invalid fixture(s) (triggered 1 invalid fixture(s))");
    }

    [Fact]
    public void ConsistencyAnalyserMessages_Should_Append_Suggestions_When_Present()
    {
        // Arrange
        const string ruleId = "rule-1";

        // Act
        var withoutSuggestion = ConsistencyAnalyserMessages.RuleTargetPathMissing(ruleId, "missingField", null);
        var withSuggestion = ConsistencyAnalyserMessages.RuleTargetPathMissing(ruleId, "shool", "school");
        var enumWithMatch = ConsistencyAnalyserMessages.EnumLiteralMismatch(ruleId, "status", "open", "Open, Closed", "Open");

        // Assert
        withoutSuggestion.Should().Be("Rule 'rule-1' target path 'missingField' does not exist in JSON Schema.");
        withSuggestion.Should().Be("Rule 'rule-1' target path 'shool' does not exist in JSON Schema. Did you mean 'school'?");
        enumWithMatch.Should().Be("Rule 'rule-1' compares field 'status' against enum literal 'open', which is not in the JSON Schema enum [Open, Closed]. Did you mean 'Open'?");
    }

    [Fact]
    public void WorkflowMessages_Should_Format_Engine_Descriptions_And_Runbook_Summaries()
    {
        // Arrange
        const string skipSuffix = "; 1 skipped";
        const string infoSuffix = "; 2 info notice(s)";

        // Act
        var jsonSchemaDesc = WorkflowMessages.JsonSchemaNetDescription(true);
        var celDesc = WorkflowMessages.CelDescription(10_000);
        var zeroViolations = WorkflowMessages.EvaluatedRulesZeroViolations(4, skipSuffix, infoSuffix);
        var withViolations = WorkflowMessages.EvaluatedRulesWithViolations(4, 3, string.Empty, string.Empty);

        // Assert
        jsonSchemaDesc.Should().Be("Validates payload structure against compiled JSON Schema (Draft 2020-12 / Draft-07) with closed-schema enforcement (format validation=True).");
        celDesc.Should().Contain("10000 max iterations.");
        zeroViolations.Should().Be("Evaluated 4 rule(s) with 0 violations; 1 skipped; 2 info notice(s)");
        withViolations.Should().Be("Evaluated 4 rule(s); 3 violation(s)");
    }

    [Fact]
    public void SubmissionMessages_Should_Format_Audit_And_Guidance_Messages()
    {
        // Arrange
        var timestamp = new DateTimeOffset(2026, 6, 15, 14, 30, 5, TimeSpan.FromHours(1));

        // Act
        var finalised = SubmissionMessages.FinalisedAtUtc(timestamp);
        var step = SubmissionMessages.ResolveIssueStep("[R1] ", "/school/urn", "Required field missing");

        // Assert
        finalised.Should().Be("Submission finalised at 2026-06-15 13:30:05 UTC and promoted for downstream processing.");
        step.Should().Be("Resolve [R1] /school/urn: Required field missing");
    }

    [Fact]
    public void AdminUiMessages_Should_Handle_Singular_Plural_And_Identifier_Formatting()
    {
        // Arrange
        var deletedAt = new DateTimeOffset(2026, 5, 1, 9, 15, 0, TimeSpan.Zero);

        // Act
        var oneField = AdminUiMessages.TopLevelFieldSummary(1);
        var manyFields = AdminUiMessages.TopLevelFieldSummary(3);
        var oneRule = AdminUiMessages.ActiveRuleSummary(1);
        var manyRules = AdminUiMessages.ActiveRuleSummary(2);
        var oneActive = AdminUiMessages.EngineActiveRulesLabel(1);
        var manyActive = AdminUiMessages.EngineActiveRulesLabel(4);
        var requiredOpt = AdminUiMessages.IdentifierOptionDisplay("/school/urn", "string", true);
        var optionalOpt = AdminUiMessages.IdentifierOptionDisplay("/school/alt", "integer", false);
        var deletedNotice = AdminUiMessages.DeletedWithTimestamp(deletedAt, 3);

        // Assert
        oneField.Should().Be("1 top-level field");
        manyFields.Should().Be("3 top-level fields");
        oneRule.Should().Be("1 active rule");
        manyRules.Should().Be("2 active rules");
        oneActive.Should().Be("Active (1 rule)");
        manyActive.Should().Be("Active (4 rules)");
        requiredOpt.Should().Be("/school/urn (string, required)");
        optionalOpt.Should().Be("/school/alt (integer, optional)");
        deletedNotice.Should().Be("Deleted 2026-05-01 09:15 UTC (push fixes to v3)");
    }
}
