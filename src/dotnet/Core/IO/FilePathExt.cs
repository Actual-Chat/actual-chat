using Stl.IO;

namespace ActualChat.IO;

public static class FilePathExt
{
    public static bool IsSubPathOf(this FilePath path, FilePath baseBath)
    {
        var relativePath = path.RelativeTo(baseBath);
        var pathValue = relativePath.Value;
        return pathValue != "."
            && pathValue != ".."
            && !pathValue.StartsWith("../")
            && !pathValue.StartsWith(@"..\")
            && !relativePath.IsRooted;
    }
}
