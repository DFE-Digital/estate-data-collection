namespace EstateDataCollection.Application.Validators.Logic;

/// <summary>
/// Async-flow ambient override for numeric comparison tolerance, allowing schema-level
/// <see cref="Domain.Schema.SchemaValidationPolicy.NumericToleranceOverride"/> to flow into
/// quality rule engines without breaking existing engine signatures or thread-safety.
/// </summary>
public static class NumericToleranceScope
{
    private static readonly AsyncLocal<double?> CurrentTolerance = new();

    public static double? Current => CurrentTolerance.Value;

    public static IDisposable Push(double? tolerance)
    {
        var previous = CurrentTolerance.Value;
        CurrentTolerance.Value = tolerance;
        return new PopScope(previous);
    }

    private sealed class PopScope(double? previous) : IDisposable
    {
        public void Dispose()
        {
            CurrentTolerance.Value = previous;
        }
    }
}
