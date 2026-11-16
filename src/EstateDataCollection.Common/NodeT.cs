namespace EstateDataCollection.Common;

public record Node<T>(T Key, string Name) where T : struct;

public static class NodeExtensions
{
    public static Node<int> ToNode(this Enum enumValue)
    {
        return new Node<int>((int)(object)enumValue, enumValue.ToString());
    }
}