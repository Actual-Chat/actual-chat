using ActualLab.IO;

namespace ActualChat.App.Server;

/// <summary>
/// Searches wwwroot folder path
/// We don't want to check and copy the wwwroot on every build to artifacts and it's not safe to
/// use <see cref="Environment.CurrentDirectory"/>, so we'll try to find directory.
/// It's normal not to store these paths, because after initialization <see cref="WebHostBuilder"/> you should use
/// their abstractions
/// </summary>
internal static class AppPathResolver
{
    private static string? _webRootPath;

    public static FilePath GetWebRootPath()
        => _webRootPath ??= FindWebRootPath();

    public static FilePath GetContentRootPath()
        => AppDomain.CurrentDomain.BaseDirectory!;

    // Private methods

    private static FilePath FindWebRootPath()
    {
        var probePaths = new List<FilePath>(4) {
            AppDomain.CurrentDomain.BaseDirectory!,
        };
        var developerMachineClientWebRootProbeDirectory = GetDeveloperMachineWebRootProbeDirectory("App.Wasm");
        if (developerMachineClientWebRootProbeDirectory.HasValue)
            probePaths.Add(developerMachineClientWebRootProbeDirectory.Value);

        var result = (
            from path in probePaths
            let wwwroot = path & "wwwroot"
            where File.Exists(wwwroot & "favicon_voxt.ico")
            select wwwroot
            ).FirstOrDefault();

        if (result.IsEmpty)
            throw new DirectoryNotFoundException(
                $"Couldn't find wwwroot directory, probed: {probePaths.ToDelimitedString("; ")}");
        return result;
    }

    private static FilePath? GetDeveloperMachineWebRootProbeDirectory(string projectName)
    {
        try {
            var solutionRoot = SolutionPaths.GetSolutionRootPath();
            return solutionRoot & "src" & "dotnet" & projectName;
        }
        catch (DirectoryNotFoundException) {
            return null;
        }
    }
}
