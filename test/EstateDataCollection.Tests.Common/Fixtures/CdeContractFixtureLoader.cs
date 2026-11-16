namespace EstateDataCollection.Tests.Common.Fixtures;

public static class CdeContractFixtureLoader
{
    public static string LoadFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "cde-data-contracts", relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate cde-data-contracts/{relativePath} from {AppContext.BaseDirectory}.");
    }
}
