namespace ActualChat.Host;

/// <summary>
/// Searches wwwroot folder path
/// We don't want to check and copy the wwwroot on every build to artifacts and it's not safe to
/// use <see cref="Environment.CurrentDirectory"/>, so we'll try to find directory.
/// It's normal not to store these paths, because after initialization <see cref="WebHostBuilder"/> you should use
/// their abstractions
/// </summary>
internal static class AppPathResolver
{
    public static string GetWebRootPath() => SearchWebRootDirectory();

    public static string GetContentRootPath() => AppDomain.CurrentDomain.BaseDirectory!;

    private static string? GetDeveloperMachineWebRootProbeDirectory(string projectName, [CallerFilePath] string? path = null)
    {
        var dirName = Path.GetDirectoryName(path);

        var projectRoot = Path.GetFullPath(!string.IsNullOrWhiteSpace(dirName) && Directory.Exists(dirName)
            ? Path.GetDirectoryName(path)!
            : Directory.GetCurrentDirectory());

        while (!projectRoot.IsNullOrEmpty() && Directory.Exists(projectRoot)) {
            var gitPath = Path.Combine(projectRoot, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath)) {
                return Path.Combine(projectRoot, "src", "dotnet", projectName);
            }
            projectRoot = Path.GetDirectoryName(projectRoot);
        }
        return null;
    }

    private static string SearchWebRootDirectory()
    {
        var probeDirectories = new List<string>(4) { AppDomain.CurrentDomain.BaseDirectory!, };
        var developerMachineClientWebRootProbeDirectory = GetDeveloperMachineWebRootProbeDirectory("App.Wasm");
        if (!string.IsNullOrWhiteSpace(developerMachineClientWebRootProbeDirectory)) {
            probeDirectories.Add(developerMachineClientWebRootProbeDirectory);
        }

        return probeDirectories
                // Web root directory is a directory where favicon.ico is located
                .Select(baseProbePath => File.Exists(Path.Combine(baseProbePath, "wwwroot", "favicon.ico"))
                    ? Path.GetFullPath(Path.Combine(baseProbePath, "wwwroot"))
                    : null)
                .FirstOrDefault(webRootPath => !webRootPath.IsNullOrEmpty())
            ?? throw new DirectoryNotFoundException(
                $"Could not find web root directory\n Searched dirs: \n{string.Join("\n", probeDirectories)}");
    }
}
