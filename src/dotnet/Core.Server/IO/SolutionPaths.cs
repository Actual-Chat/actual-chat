using ActualLab.IO;

namespace ActualChat.IO;

/// <summary>
/// Provides paths relative to the solution root.
/// Used by App.Server and App.AspireHost for loading .env files.
/// </summary>
public static class SolutionPaths
{
    private static FilePath? _solutionRootPath;

    public static FilePath GetSolutionRootPath([CallerFilePath] string? callerPath = null)
        => _solutionRootPath ??= FindSolutionRootPath(callerPath);

    public static FilePath GetDotEnvFilePath()
        => GetSolutionRootPath() | ".env";

    private static FilePath FindSolutionRootPath(FilePath callerPath)
        => callerPath.DirectoryPath
            .SelfAndAncestors()
            .FirstOrDefault(dir => (dir & ".git").DirectoryExists);
}
