namespace EstateDataCollection.Common.Markers;

public interface IOption<TSelf> where TSelf : class, IOption<TSelf>
{
    static abstract string SectionName { get; }
}