namespace EstateDataCollection.Common;

/// <summary>
/// Key/name pair representing an enumerated lookup or lifecycle state in API responses.
/// </summary>
/// <typeparam name="T">Underlying integral key type.</typeparam>
/// <param name="Key">Numeric identifier of the node.</param>
/// <param name="Name">Human-readable display name of the node.</param>
public record Node<T>(T Key, string Name) where T : struct;
