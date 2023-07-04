using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Build;

internal static class Utils {
    public static string FindDotnetExe()
        => TryFindDotNetExePath()
            ?? throw new FileNotFoundException("'dotnet' command isn't found. Try to set DOTNET_ROOT variable.");

    public static string GithubLogger()
        => IsGitHubActions() ? "--logger GitHubActions " : "";

    private static bool IsGitHubActions()
        => bool.TryParse(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), out bool isGitHubActions) && isGitHubActions;

    public static string FindNpmExe()
        => TryFindCommandPath("npm")
            ?? throw new WithoutStackException(new FileNotFoundException("'npm' command isn't found. Install nodejs from https://nodejs.org/"));

    private static string? TryFindDotNetExePath()
    {
        var dotnet = "dotnet";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            dotnet += ".exe";

        var mainModule = Process.GetCurrentProcess().MainModule;
        if (!string.IsNullOrEmpty(mainModule?.FileName) && Path.GetFileName(mainModule.FileName)!.Equals(dotnet, StringComparison.OrdinalIgnoreCase))
            return mainModule.FileName;

        var environmentVariable = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(environmentVariable))
            return Path.Combine(environmentVariable, dotnet);

        var paths = Environment.GetEnvironmentVariable("PATH");
        if (paths == null)
            return null;

        foreach (var path in paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            var fullPath = Path.Combine(path, dotnet);
            if (File.Exists(fullPath))
                return fullPath;
        }
        return null;
    }

    /// <summary>
    /// Returns full path for short commands like "npm" (on windows it will be 'C:\Program Files\nodejs\npm.cmd' for example)
    /// or null if full path not found
    /// </summary>
    private static string? TryFindCommandPath(string cmd)
    {
        if (File.Exists(cmd)) {
            return Path.GetFullPath(cmd);
        }

        var values = Environment.GetEnvironmentVariable("PATH");
        if (values == null)
            return null;

        var isWindows = Environment.OSVersion.Platform != PlatformID.Unix;

        foreach (var path in values.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            var fullPath = Path.Combine(path, cmd);
            if (isWindows) {
                if (File.Exists(fullPath + ".exe"))
                    return fullPath + ".exe";
                else if (File.Exists(fullPath + ".cmd"))
                    return fullPath + ".cmd";
                else if (File.Exists(fullPath + ".bat"))
                    return fullPath + ".bat";
            }
            else {
                if (File.Exists(fullPath + ".sh"))
                    return fullPath + ".sh";
            }
            if (File.Exists(fullPath))
                return fullPath;
        }
        return null;
    }
}
