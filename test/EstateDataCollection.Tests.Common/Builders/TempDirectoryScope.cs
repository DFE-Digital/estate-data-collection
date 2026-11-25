namespace EstateDataCollection.Tests.Common.Builders;

public sealed class TempDirectoryScope : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"edc-tests-{Guid.NewGuid():N}");

    public TempDirectoryScope()
    {
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of test temp folder.
        }
    }
}
