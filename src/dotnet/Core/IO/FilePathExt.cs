using ActualChat.Hashing;
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

    public static FilePath RequireFileExists(this FilePath path, [CallerArgumentExpression(nameof(path))] string name = "")
    {
        if (!File.Exists(path))
            throw StandardError.NotFound<FilePath>($"{name} '{path}' does not exist.");
        return path;
    }

    public static FilePath RequireHash(this FilePath path, string expectedHash, bool trim = true, [CallerArgumentExpression(nameof(path))] string name = "")
    {
        var text = File.ReadAllText(path);
        if (trim)
            text = text.Trim();
        var hash = text.Hash().SHA256().AlphaNumeric();
        if (!OrdinalEquals(hash, expectedHash))
            throw StandardError.Configuration($"{name} ('{path}'): hash '{hash}' does not match '{expectedHash}'.");

        return path;
    }
}
