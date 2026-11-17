using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.AzureFunctions.AdminUi.Helpers;

public sealed record ParsedWorkbenchInput(
    string InputMode,
    string SchemaName,
    int Version,
    string FileName,
    string OdcsYaml,
    string SchemaJson,
    string RulesJson,
    string ValidFixtureJson,
    string InvalidFixtureJson,
    IReadOnlyList<string> ValidFixtures,
    IReadOnlyList<string> InvalidFixtures,
    SchemaIdentifierConfig IdentifierConfig,
    string? RequestedPropertySelection = null,
    string? RequestedIdentifierLabel = null)
{
    public static (string Mode, string SelectedProperty, string Pointer, string Label) ResolveUiSelection(
        SchemaIdentifierConfig? config,
        IReadOnlyList<SchemaIdentifierPropertyOption> availableProperties,
        string? explicitPropertySelection = null,
        string? explicitLabelOverride = null)
    {
        if (config?.IsDisabled == true)
        {
            return ("none", IdentifierPropertyDiscovery.CustomOptionValue, string.Empty, string.Empty);
        }

        var pointer = config?.JsonPointer?.Trim() ?? string.Empty;
        var label = !string.IsNullOrWhiteSpace(explicitLabelOverride)
            ? explicitLabelOverride.Trim()
            : (!string.IsNullOrWhiteSpace(pointer) ? config?.Type?.Trim() ?? string.Empty : string.Empty);

        if (!string.IsNullOrWhiteSpace(explicitPropertySelection))
        {
            if (string.Equals(explicitPropertySelection, IdentifierPropertyDiscovery.CustomOptionValue, StringComparison.OrdinalIgnoreCase))
            {
                return ("enabled", IdentifierPropertyDiscovery.CustomOptionValue, pointer, label);
            }

            var matchedExplicit = availableProperties.FirstOrDefault(p =>
                string.Equals(p.Pointer, explicitPropertySelection, StringComparison.OrdinalIgnoreCase));
            if (matchedExplicit is not null)
            {
                return ("enabled", matchedExplicit.Pointer, matchedExplicit.Pointer, label);
            }
        }

        if (string.IsNullOrWhiteSpace(pointer))
        {
            return ("enabled", IdentifierPropertyDiscovery.CustomOptionValue, string.Empty, label);
        }

        var matchedProperty = availableProperties.FirstOrDefault(p =>
            string.Equals(p.Pointer, pointer, StringComparison.OrdinalIgnoreCase));

        if (matchedProperty is not null)
        {
            return ("enabled", matchedProperty.Pointer, matchedProperty.Pointer, label);
        }

        return ("enabled", IdentifierPropertyDiscovery.CustomOptionValue, pointer, label);
    }

    public WorkbenchViewModel ToViewModel(
        AdminPageMeta page,
        WorkbenchVerifyViewModel? verification = null,
        Error? pageError = null)
    {
        var availableProperties = IdentifierPropertyDiscovery.FromRawInputs(InputMode, SchemaJson, OdcsYaml);
        var resolvedConfig = verification?.Result?.ResolvedIdentifierConfig ?? IdentifierConfig;
        var (mode, selectedProp, pointer, label) = ResolveUiSelection(
            resolvedConfig,
            availableProperties,
            RequestedPropertySelection,
            RequestedIdentifierLabel);

        var resolvedRequired = verification?.Result?.ResolvedIdentifierConfig?.Required ?? IdentifierConfig.Required;

        return new WorkbenchViewModel(
            Page: page,
            InputMode: InputMode,
            SchemaName: SchemaName,
            Version: Version,
            FileName: FileName,
            OdcsYaml: OdcsYaml,
            SchemaJson: SchemaJson,
            RulesJson: RulesJson,
            ValidFixtureJson: ValidFixtureJson,
            InvalidFixtureJson: InvalidFixtureJson,
            IdentifierMode: mode,
            IdentifierProperty: selectedProp,
            IdentifierPointer: pointer,
            IdentifierLabel: label,
            IdentifierRequired: resolvedRequired,
            AvailableIdentifierProperties: availableProperties,
            Verification: verification,
            PageError: pageError);
    }
}
