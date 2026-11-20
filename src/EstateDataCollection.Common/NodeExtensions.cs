namespace EstateDataCollection.Common;

public static class NodeExtensions
{
    public static Node<int> ToNode(this Enum enumValue)
    {
        return new Node<int>((int)(object)enumValue, enumValue.ToString());
    }
}