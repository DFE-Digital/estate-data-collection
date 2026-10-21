namespace EstateDataCollection.Common;

public record Node<T>(T Key, string Name) where T : struct;

public static class NodeExtensions
{
    public static Node<int> ToNode(this Enum enumValue) => new((int)(object)enumValue, enumValue.ToString());
}