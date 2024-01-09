using ActualLab.IO;

namespace ActualChat.IO;

public static class FilePathExt
{
    public static bool IsSubPathOf(this FilePath path, FilePath baseBath)
    {
        var relativePath = path.RelativeTo(baseBath);
        var pathValue = relativePath.Value;
        return !OrdinalEquals(pathValue, ".")
            && !OrdinalEquals(pathValue, "..")
            && !pathValue.OrdinalStartsWith("../")
            && !pathValue.OrdinalStartsWith(@"..\")
            && !relativePath.IsRooted;
    }
}
