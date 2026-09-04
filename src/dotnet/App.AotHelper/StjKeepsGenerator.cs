using System.Text;
using static System.Console;

namespace ActualChat.App.AotHelper;

/// <summary>
/// Generates BlazorUIStjKeeps.g.cs — a partial of BlazorUIModuleInitializer
/// containing CodeKeeper.Keep(string) calls for all STJ internal converter types
/// needed for Native AOT JS interop.
/// </summary>
public static class StjKeepsGenerator
{
    private static readonly string[] AssemblyNames = [
        "ActualChat.Core",
        "ActualChat.Api",
        "ActualChat.Api.Contracts",
        "ActualChat.UI.Blazor",
        "ActualChat.UI.Blazor.App",
    ];

    public static int Generate(string? projectRoot)
    {
        projectRoot ??= FindProjectRoot();
        if (projectRoot == null) {
            Error.WriteLine("Error: Could not find project root.");
            return 1;
        }

        var srcDotnet = Path.Combine(projectRoot, "src", "dotnet");
        if (!Directory.Exists(srcDotnet)) {
            Error.WriteLine($"Error: src/dotnet not found at: {srcDotnet}");
            return 1;
        }

        // Load assemblies
        LoadAssemblies();

        // Discover converters
        WriteLine("Discovering STJ converter types...");
        var converterAqns = StjConverterDiscovery.DiscoverAll();
        WriteLine($"Discovered {converterAqns.Count} converter types");

        // Generate file
        var code = StjConverterDiscovery.GenerateKeepsFile(converterAqns);
        var outputPath = Path.Combine(srcDotnet, "UI.Blazor", "Module", "BlazorUIStjKeeps.g.cs");
        // StringBuilder.AppendLine emits CRLF on Windows, but .gitattributes stores
        // *.cs with LF - without this the file is rewritten dirty on every run.
        File.WriteAllText(outputPath, code.ReplaceLineEndings("\n"), Encoding.UTF8);
        WriteLine($"Generated: {outputPath}");

        return 0;
    }

    private static string? FindProjectRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null) {
            if (File.Exists(Path.Combine(dir, "ActualChat.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static void LoadAssemblies()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in AssemblyNames) {
            if (loaded.Contains(name))
                continue;
            try {
                var assembly = Assembly.Load(name);
                WriteLine($"Loaded: {assembly.GetName().Name}");
            }
            catch (Exception e) {
                Error.WriteLine($"Warning: Could not load {name}: {e.Message}");
            }
        }
    }
}
