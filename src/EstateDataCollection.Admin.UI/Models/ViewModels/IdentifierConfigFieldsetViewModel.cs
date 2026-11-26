using EstateDataCollection.Common.Messages;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record IdentifierConfigFieldsetViewModel(
    bool IsEnabled,
    bool IsNone,
    bool IsCustomPath,
    bool HidePointerGroup,
    string CustomOptionValue,
    string ActivePointer,
    string PointerPreview,
    string LabelInputValue,
    string LabelPreview,
    string LabelPlaceholder,
    bool HasExplicitOverride,
    string RequiredPreview,
    IReadOnlyList<IdentifierPropertySelectOptionViewModel> Options)
{
    public static IdentifierConfigFieldsetViewModel Create(
        string mode,
        string selectedProperty,
        string pointer,
        string label,
        bool? required,
        IReadOnlyList<SchemaIdentifierPropertyOption>? availableProperties)
    {
        var props = availableProperties ?? [];
        var isNone = string.Equals(mode, "none", StringComparison.OrdinalIgnoreCase);
        var selectedPropValue = string.IsNullOrWhiteSpace(selectedProperty)
            ? IdentifierPropertyDiscovery.CustomOptionValue
            : selectedProperty;

        var matchedProp = props.FirstOrDefault(p =>
            string.Equals(p.Pointer, selectedPropValue, StringComparison.OrdinalIgnoreCase));

        var isCustomPath = matchedProp is null;

        var activePointer = isCustomPath
            ? pointer
            : matchedProp!.Pointer;

        var pointerPreview = isNone
            ? AdminUiMessages.IdentifierPointerNoneDisabled
            : (!string.IsNullOrWhiteSpace(activePointer)
                ? activePointer
                : AdminUiMessages.IdentifierPointerNotSet);

        var autoDerivedLabel = IdentifierPropertyDiscovery.DeriveTypeFromPointerOrNull(activePointer);
        var trimmedLabel = label?.Trim() ?? string.Empty;
        var hasExplicitOverride = !string.IsNullOrWhiteSpace(trimmedLabel)
            && !string.Equals(trimmedLabel, autoDerivedLabel, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trimmedLabel, SchemaIdentifierConfig.NoneType, StringComparison.OrdinalIgnoreCase);

        var labelInputValue = hasExplicitOverride ? trimmedLabel : string.Empty;
        var labelPreview = isNone
            ? AdminUiMessages.IdentifierLabelNone
            : (hasExplicitOverride
                ? trimmedLabel
                : (autoDerivedLabel ?? AdminUiMessages.IdentifierLabelDerivedFromProperty));

        bool? effectiveRequired = isNone
            ? false
            : (required ?? matchedProp?.Required);

        var requiredPreview = isNone
            ? AdminUiMessages.IdentifierRequiredNoDisabled
            : effectiveRequired switch
            {
                true => AdminUiMessages.IdentifierRequiredYesSchema,
                false => AdminUiMessages.IdentifierRequiredNoSchema,
                null => AdminUiMessages.IdentifierRequiredAutoDerived
            };

        var labelPlaceholder = autoDerivedLabel is not null
            ? AdminUiMessages.IdentifierLabelAutoPlaceholder(autoDerivedLabel)
            : AdminUiMessages.IdentifierLabelPlaceholderFallback;

        var options = props
            .Select(p => new IdentifierPropertySelectOptionViewModel(
                Pointer: p.Pointer,
                DerivedType: p.DerivedType,
                RequiredAttributeValue: p.Required ? "true" : "false",
                IsSelected: string.Equals(selectedPropValue, p.Pointer, StringComparison.OrdinalIgnoreCase),
                DisplayText: AdminUiMessages.IdentifierOptionDisplay(p.Pointer, p.ValueType, p.Required)))
            .ToList();

        return new IdentifierConfigFieldsetViewModel(
            IsEnabled: !isNone,
            IsNone: isNone,
            IsCustomPath: isCustomPath,
            HidePointerGroup: isNone || !isCustomPath,
            CustomOptionValue: IdentifierPropertyDiscovery.CustomOptionValue,
            ActivePointer: activePointer ?? string.Empty,
            PointerPreview: pointerPreview,
            LabelInputValue: labelInputValue,
            LabelPreview: labelPreview,
            LabelPlaceholder: labelPlaceholder,
            HasExplicitOverride: hasExplicitOverride,
            RequiredPreview: requiredPreview,
            Options: options);
    }
}
