using ActualLab.IO;

namespace ActualChat.ContentCaching.UnitTests;

internal static class TestDirectory
{
    public static FilePath New()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(directory.FullName, "src", "dotnet", "ContentCaching")))
            directory = directory.Parent ?? throw new DirectoryNotFoundException("Cannot locate the project root.");

        return new FilePath(directory.FullName) & "tmp" & $"content-{Guid.NewGuid():N}";
    }
}
