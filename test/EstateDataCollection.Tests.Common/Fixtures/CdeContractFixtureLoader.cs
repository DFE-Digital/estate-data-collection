namespace EstateDataCollection.Tests.Common.Fixtures;

public static class CdeContractFixtureLoader
{
    public static string LoadFile(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var outputCandidate = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CdeContracts", normalized);
        if (File.Exists(outputCandidate))
        {
            return File.ReadAllText(outputCandidate);
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var repoFixtureCandidate = Path.Combine(dir.FullName, "test", "EstateDataCollection.Tests.Common", "Fixtures", "CdeContracts", normalized);
            if (File.Exists(repoFixtureCandidate))
            {
                return File.ReadAllText(repoFixtureCandidate);
            }

            var legacyCandidate = Path.Combine(dir.FullName, "cde-data-contracts", normalized);
            if (File.Exists(legacyCandidate))
            {
                return File.ReadAllText(legacyCandidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate contract test fixture '{relativePath}' from {AppContext.BaseDirectory}.");
    }
}
