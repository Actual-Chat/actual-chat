using System.Diagnostics;
using System.Xml.Linq;

namespace Build.Commands;

/// <summary>
/// Stages the MSIX layout Visual Studio and Rider deploy - <c>&lt;output&gt;/AppX</c> - from the
/// <c>*.build.appxrecipe</c> a packaged build writes. <c>dotnet build</c> never creates it.
/// </summary>
internal static class AppxLayout
{
    private static readonly XNamespace MsBuild = "http://schemas.microsoft.com/developer/msbuild/2003";

    public static int Stage(string outputDir)
    {
        var recipePath = Directory.GetFiles(Path.GetFullPath(outputDir), "*.build.appxrecipe").SingleOrDefault()
            ?? throw new WithoutStackException($"No *.build.appxrecipe in {outputDir} - the build isn't packaged.");
        var layoutDir = Path.GetFullPath(GetDir(outputDir));
        var files = ReadRecipe(recipePath);
        // The recipe misses Platforms/Windows/App.xbf, and WinUI crashes on start without it
        foreach (var path in Directory.EnumerateFiles(outputDir, "*", SearchOption.AllDirectories)) {
            var fullPath = Path.GetFullPath(path);
            if (IsUnder(fullPath, layoutDir) || fullPath == recipePath)
                continue;

            files.TryAdd(Path.GetRelativePath(outputDir, path), fullPath);
        }

        StopRunningApp(layoutDir);
        var copyCount = 0;
        foreach (var (packagePath, sourcePath) in files) {
            var target = new FileInfo(Path.Combine(layoutDir, packagePath));
            var source = new FileInfo(sourcePath);
            if (target.Exists && target.Length == source.Length && target.LastWriteTimeUtc == source.LastWriteTimeUtc)
                continue;

            target.Directory!.Create();
            // The shell keeps resources.pri of a registered package memory-mapped, which blocks
            // overwriting it but not deleting it
            if (target.Exists)
                target.Delete();
            source.CopyTo(target.FullName);
            copyCount++;
        }
        var deleteCount = 0;
        if (Directory.Exists(layoutDir))
            foreach (var path in Directory.EnumerateFiles(layoutDir, "*", SearchOption.AllDirectories)) {
                if (files.ContainsKey(Path.GetRelativePath(layoutDir, path)))
                    continue;

                File.Delete(path);
                deleteCount++;
            }
        Console.WriteLine($"{files.Count} files in the layout, {copyCount} copied, {deleteCount} removed.");
        return 0;
    }

    public static string GetDir(string outputDir)
        => Path.Combine(outputDir, "AppX");

    // Private methods

    private static Dictionary<string, string> ReadRecipe(string recipePath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var items = XDocument.Load(recipePath)
            .Descendants()
            .Where(x => x.Name == MsBuild + "AppxPackagedFile" || x.Name == MsBuild + "AppXManifest");
        foreach (var item in items) {
            var packagePath = item.Element(MsBuild + "PackagePath")?.Value;
            if (packagePath is { Length: > 0 } && item.Attribute("Include")?.Value is { Length: > 0 } sourcePath)
                result[packagePath] = sourcePath;
        }
        return result;
    }

    private static void StopRunningApp(string layoutDir)
    {
        // A running instance locks its exe and DLLs, so it has to go before they're overwritten
        foreach (var process in Process.GetProcesses()) {
            using var _ = process;
            string? path;
            try {
                path = process.MainModule?.FileName;
            }
            catch {
                continue;
            }

            if (path is null || !IsUnder(path, layoutDir))
                continue;

            process.Kill();
            process.WaitForExit(10_000);
        }
    }

    private static bool IsUnder(string path, string dir)
        => path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
