using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Build;

public class FileExt
{
    public static void Remove(params string[] patterns)
    {
        var matcher = new Matcher();
        matcher.AddIncludePatterns(patterns);
        matcher.GetResultsInFullPath(Environment.CurrentDirectory);
        var match = matcher.Execute(new DirectoryInfoWrapper(new (Environment.CurrentDirectory)));
        if (!match.HasMatches)
            return;

        foreach (var file in match.Files)
            try {
                File.Delete(file.Path);
            }
            catch (Exception e) {
                Console.Error.WriteLine($"Failed to remove '{file}'.{e}");
            }
    }
}
