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
            ? "None (disabled)"
            : (!string.IsNullOrWhiteSpace(activePointer)
                ? activePointer
                : "Not set — enter or select a property");

        var autoDerivedLabel = IdentifierPropertyDiscovery.DeriveTypeFromPointerOrNull(activePointer);
        var trimmedLabel = label?.Trim() ?? string.Empty;
        var hasExplicitOverride = !string.IsNullOrWhiteSpace(trimmedLabel)
            && !string.Equals(trimmedLabel, autoDerivedLabel, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trimmedLabel, SchemaIdentifierConfig.NoneType, StringComparison.OrdinalIgnoreCase);

        var labelInputValue = hasExplicitOverride ? trimmedLabel : string.Empty;
        var labelPreview = isNone
            ? "None"
            : (hasExplicitOverride
                ? trimmedLabel
                : (autoDerivedLabel ?? "Derived from property name"));

        bool? effectiveRequired = isNone
            ? false
            : (required ?? matchedProp?.Required);

        var requiredPreview = isNone
            ? "No (identifier partitioning disabled)"
            : effectiveRequired switch
            {
                true => "Yes (required by JSON Schema)",
                false => "No (optional in JSON Schema)",
                null => "Derived automatically from JSON Schema 'required' on Verify / Save"
            };

        var labelPlaceholder = autoDerivedLabel is not null
            ? $"Auto: {autoDerivedLabel}"
            : "Derived from property";

        var options = props
            .Select(p => new IdentifierPropertySelectOptionViewModel(
                Pointer: p.Pointer,
                DerivedType: p.DerivedType,
                RequiredAttributeValue: p.Required ? "true" : "false",
                IsSelected: string.Equals(selectedPropValue, p.Pointer, StringComparison.OrdinalIgnoreCase),
                DisplayText: $"{p.Pointer} ({p.ValueType}, {(p.Required ? "required" : "optional")})"))
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
