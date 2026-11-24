namespace EstateDataCollection.Admin.UI.Models;

public sealed record SchemaIdentifierPropertyOption(
    string Pointer,
    string Name,
    string ValueType,
    bool Required,
    string DerivedType);
