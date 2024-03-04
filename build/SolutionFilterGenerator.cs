using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Build;

public static class SolutionFilterGenerator
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new () {
        WriteIndented = true,
    };

    public static async Task Generate()
    {
        var files = Directory.EnumerateFiles("src/dotnet", "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles("tests", "*.csproj", SearchOption.AllDirectories))
            .Where(x => !x.Contains("Maui", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Replace('/', '\\'))
            .ToList();
        files.Sort();
        await WriteToFile(files, "ActualChat.CI.slnf").ConfigureAwait(false);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    private static async Task WriteToFile(IReadOnlyCollection<string> csprojFiles, string slnfFilePath)
    {
        File.Delete(slnfFilePath);
        var stream = File.OpenWrite(slnfFilePath);
        await using var __ = stream.ConfigureAwait(false);
        await JsonSerializer.SerializeAsync(stream,
                new {
                    solution = new {
                        path = "ActualChat.sln",
                        projects = csprojFiles,
                    },
                },
                JsonSerializerOptions)
            .ConfigureAwait(false);
    }
}
