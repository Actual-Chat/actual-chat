using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Build;

public static class FileExt
{
    public static void Remove(params string[] patterns)
    {
        var workingDir = Environment.CurrentDirectory;
        var dirPatterns = patterns.Where(x => !x.Contains('*') && Directory.Exists(Path.Combine(workingDir, x))).ToList();
        var filePatterns = patterns.Except(dirPatterns).ToList();

        RemoveDirs(dirPatterns, workingDir);
        RemoveWithGlob(filePatterns, workingDir);
    }

    private static void RemoveDirs(List<string> dirPatterns, string workingDir)
    {
        foreach (var dirPattern in dirPatterns)
            try
            {
                var dirPath = Path.Combine(workingDir, dirPattern);
                Directory.Delete(dirPath, true);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to remove '{dirPattern}'.{e}");
            }
    }

    private static void RemoveWithGlob(IReadOnlyCollection<string> filePatterns, string workingDir)
    {
        if (filePatterns.Count <= 0)
            return;

        var matcher = new Matcher();
        matcher.AddIncludePatterns(filePatterns);
        var match = matcher.Execute(new DirectoryInfoWrapper(new(workingDir)));
        if (!match.HasMatches)
            return;

        foreach (var file in match.Files)
            try
            {
                var path = Path.Combine(workingDir, file.Path);
                Console.WriteLine($"Removing {file.Path}");
                File.Delete(path);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to remove '{file}'.{e}");
            }
    }
}
