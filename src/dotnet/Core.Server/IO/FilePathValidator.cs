using ActualLab.IO;

namespace ActualChat.IO;

public static class FilePathValidator
{
    public static FilePath GetContainedPath(FilePath baseDirectory, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        if (relativePath.Length == 0
            || Path.IsPathRooted(relativePath)
            || IsWindowsDrivePath(relativePath)
            || relativePath.Contains('\\')
            || OperatingSystem.IsWindows() && relativePath.Contains(':')
            || relativePath.Any(char.IsControl))
            throw StandardError.Constraint("The path must be a relative descendant of the base directory.");

        var segments = relativePath.Split('/');
        if (segments.Any(x => x is "." or ".." || IsDeviceName(x)))
            throw StandardError.Constraint("The path must be a relative descendant of the base directory.");

        var normalizedBase = Path.GetFullPath(baseDirectory.Value);
        var normalizedPath = Path.GetFullPath(relativePath, normalizedBase);
        var basePrefix = Path.EndsInDirectorySeparator(normalizedBase)
            ? normalizedBase
            : normalizedBase + Path.DirectorySeparatorChar;
        var isContained = OperatingSystem.IsWindows()
            ? normalizedPath.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase)
            : normalizedPath.StartsWith(basePrefix);
        if (!isContained)
            throw StandardError.Constraint("The path must be a relative descendant of the base directory.");

        return normalizedPath;
    }

    // Private methods

    private static bool IsWindowsDrivePath(string path)
        => path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    private static bool IsDeviceName(string segment)
    {
        var name = segment.Split(['.', ':'], 2)[0].TrimEnd(' ');
        if (name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase))
            return true;

        return name.Length == 4
            && name[3] is >= '1' and <= '9'
            && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }
}
