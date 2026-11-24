using System.Xml.Linq;

internal static class ArchitectureChecks
{
    public static void Run()
    {
        var repositoryRoot = FindRepositoryRoot();
        var modulesRoot = Path.Combine(repositoryRoot, "backend", "src", "Modules");

        foreach (var projectPath in Directory.EnumerateFiles(modulesRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var module = Path.GetRelativePath(modulesRoot, projectPath).Split(Path.DirectorySeparatorChar)[0];
            var references = XDocument.Load(projectPath).Descendants("ProjectReference")
                .Select(reference => (string?)reference.Attribute("Include"))
                .Where(reference => !string.IsNullOrWhiteSpace(reference));

            foreach (var reference in references)
            {
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, reference!));
                var relativeTarget = Path.GetRelativePath(modulesRoot, target).Replace('\\', '/');
                var targetParts = relativeTarget.Split('/');
                var targetsInfrastructure = targetParts.Any(part => part.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase));
                if (targetsInfrastructure && targetParts[0] != module)
                    throw new Exception($"{Path.GetRelativePath(repositoryRoot, projectPath)} references another module's infrastructure: {relativeTarget}");
            }
        }

        var sharedRoot = Path.Combine(repositoryRoot, "backend", "src", "Shared");
        foreach (var sourcePath in Directory.EnumerateFiles(sharedRoot, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(sourcePath);
            if (source.Contains("FoodVision.Api", StringComparison.Ordinal) ||
                source.Contains("FoodVision.VideoAnalytics", StringComparison.Ordinal) ||
                source.Contains("FoodVision.Tables", StringComparison.Ordinal) ||
                source.Contains("FoodVision.Benchmark", StringComparison.Ordinal))
                throw new Exception($"Shared project contains module-specific code: {Path.GetRelativePath(repositoryRoot, sourcePath)}");
        }

        Console.WriteLine("Architecture dependency checks passed.");
    }

    private static string FindRepositoryRoot()
    {
        for (var candidate = new DirectoryInfo(Directory.GetCurrentDirectory()); candidate is not null; candidate = candidate.Parent)
            if (File.Exists(Path.Combine(candidate.FullName, "backend", "FoodVision.slnx"))) return candidate.FullName;

        throw new DirectoryNotFoundException("Could not locate the repository root from the current directory.");
    }
}
