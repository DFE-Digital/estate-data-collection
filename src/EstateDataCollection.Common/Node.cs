namespace EstateDataCollection.Common;

public record Node<T>(T Key, string Name) where T : struct;