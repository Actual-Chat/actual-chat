using ActualLab.IO;

namespace ActualChat.IO;

/// <summary>
/// Provides paths relative to the solution root.
/// Used by App.Server for locating wwwroot in development.
/// </summary>
public static class SolutionPaths
{
    private static FilePath? _solutionRootPath;

    public static FilePath GetSolutionRootPath([CallerFilePath] string? callerPath = null)
        => _solutionRootPath ??= FindSolutionRootPath(callerPath);

    private static FilePath FindSolutionRootPath(FilePath callerPath)
        => callerPath.DirectoryPath
            .SelfAndAncestors()
            .FirstOrDefault(dir => (dir & ".git").Exists); // .git is a directory (repo) or file (worktree)
}
