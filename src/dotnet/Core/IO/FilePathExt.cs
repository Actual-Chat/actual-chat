using ActualChat.Hashing;
using ActualLab.Generators;
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

    public static FilePath RequireFileExists(
        this FilePath path,
        [CallerArgumentExpression(nameof(path))] string name = "")
        => !File.Exists(path) ? throw StandardError.NotFound<FilePath>($"{name} '{path}' does not exist.") : path;

    public static FilePath RequireHash(this FilePath path, string expectedHash, bool trim = true, [CallerArgumentExpression(nameof(path))] string name = "")
    {
        var text = File.ReadAllText(path);
        if (trim)
            text = text.Trim();
        var hash = text.Hash().SHA256().AlphaNumeric();
        return !OrdinalEquals(hash, expectedHash)
            ? throw StandardError.Configuration($"{name} ('{path}'): hash '{hash}' does not match '{expectedHash}'.")
            : path;
    }

    public static FilePath ToUnique(this FilePath path, int randomLength = 5)
        => path.DirectoryPath | $"{path.FileNameWithoutExtension}-{RandomStringGenerator.Default.Next(randomLength)}.{path.Extension}";

    public static FilePath EnsureExt(this FilePath path, string ext)
        => path.HasExtension && OrdinalEquals(path.Extension, ext) ? path : path.ChangeExtension(ext);
}
