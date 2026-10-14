using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Build;

public static class SolutionFilterGenerator
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new () {
        WriteIndented = true,
    };

    public static Task Generate()
        => WriteToFile(ListProjects(), "ActualChat.CI.slnf");

    public static async Task<string> GenerateForTests(string name, Func<string, bool> mustRunTestProject)
    {
        // dotnet test starts a test host for every test project of the solution it gets, and its test
        // filter drops a project only once that host has loaded it - so the test projects are picked here
        var slnfFilePath = Path.Combine("artifacts", "slnf", name + ".slnf");
        var files = ListProjects()
            .Where(x => !IsTestProject(x) || mustRunTestProject(GetProjectName(x)))
            .ToList();
        await WriteToFile(files, slnfFilePath).ConfigureAwait(false);
        return slnfFilePath;
    }

    // Private methods

    private static List<string> ListProjects()
    {
        var files = Directory.EnumerateFiles("src/dotnet", "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles("tests", "*.csproj", SearchOption.AllDirectories))
            .Where(x => !x.Contains("Maui", StringComparison.OrdinalIgnoreCase)
                && !x.Contains("App.Wasm", StringComparison.OrdinalIgnoreCase)
                && !x.Contains("App.Server", StringComparison.OrdinalIgnoreCase)
                && !x.Contains("App.ConsoleClient", StringComparison.OrdinalIgnoreCase)
                && !x.Contains("App.AspireHost", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Replace('/', '\\'))
            .ToList();
        files.Sort();
        return files;
    }

    [UnconditionalSuppressMessage("Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access "
        + "otherwise can break functionality when trimming application code",
        Justification = "<Pending>")]
    private static async Task WriteToFile(IReadOnlyCollection<string> csprojFiles, string slnfFilePath)
    {
        var slnfDir = Path.GetDirectoryName(Path.GetFullPath(slnfFilePath))!;
        Directory.CreateDirectory(slnfDir);
        File.Delete(slnfFilePath);
        var stream = File.OpenWrite(slnfFilePath);
        await using var __ = stream.ConfigureAwait(false);
        await JsonSerializer.SerializeAsync(stream,
                new {
                    solution = new {
                        // Relative to the filter itself, while the project paths are relative to the solution
                        path = Path.GetRelativePath(slnfDir, "ActualChat.sln"),
                        projects = csprojFiles,
                    },
                },
                JsonSerializerOptions)
            .ConfigureAwait(false);
    }

    private static bool IsTestProject(string csprojFile)
        // Testing and Testing.Host are libraries the test projects share, so they don't count
        => csprojFile.StartsWith("tests\\") && GetProjectName(csprojFile).EndsWith("Tests");

    private static string GetProjectName(string csprojFile)
        => Path.GetFileNameWithoutExtension(csprojFile.Replace('\\', '/'));
}
