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
        return pathValue != "."
            && pathValue != ".."
            && !pathValue.StartsWith("../")
            && !pathValue.StartsWith(@"..\")
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
        return hash != expectedHash
            ? throw StandardError.Configuration($"{name} ('{path}'): hash '{hash}' does not match '{expectedHash}'.")
            : path;
    }

    public static FilePath ToUnique(this FilePath path, bool ensureNotExists = true, int randomLength = 5)
    {
        var uniquePath = path.DirectoryPath | $"{path.FileNameWithoutExtension}-{RandomStringGenerator.Default.Next(randomLength)}{path.Extension}";
        if (!ensureNotExists)
            return uniquePath;

        for (var i = 1; i < 100 && File.Exists(uniquePath); i++)
            uniquePath = path.DirectoryPath | $"{path.FileNameWithoutExtension}-{RandomStringGenerator.Default.Next(randomLength)}{path.Extension}";
        return uniquePath;
    }

    // Finder-style uniquing for user-facing saves: the path itself when free, otherwise
    // "name (2).ext", "name (3).ext", ... - unlike ToUnique, which always adds a random suffix.
    public static FilePath ToUniqueNumbered(this FilePath path)
    {
        var uniquePath = path;
        for (var i = 2; uniquePath.FileExists; i++)
            uniquePath = path.DirectoryPath | $"{path.FileNameWithoutExtension} ({i}){path.Extension}";
        return uniquePath;
    }

    public static FilePath EnsureExt(this FilePath path, string ext)
        => path.HasExtension(ext) ? path : path.ChangeExtension(ext);

    public static async Task CopyFile(this FilePath sourcePath, FilePath targetPath, CancellationToken cancellationToken = default)
    {
        sourcePath.RequireFileExists();
        var source = File.OpenRead(sourcePath);
        await using var _ = source.ConfigureAwait(false);
        await source.CopyToFile(targetPath, cancellationToken).ConfigureAwait(false);
    }

    extension(FilePath path)
    {
        public long FileSize => path.GetFileInfo().Length;
        public bool FileExists => File.Exists(path);
        public bool DirectoryExists => Directory.Exists(path);
        public bool Exists => Path.Exists(path);

        public bool HasExtension(string ext)
            => string.Equals(path.Extension.EnsurePrefix("."), ext, StringComparison.OrdinalIgnoreCase);

        public void DeleteSilently()
        {
            try {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch {
                // ignore
            }
        }

        public IEnumerable<FilePath> SelfAndAncestors()
        {
            var current = path.FullPath;
            while (!current.IsEmpty) {
                yield return current;
                var parent = current.DirectoryPath;
                if (parent == current)
                    break; // Reached root
                current = parent;
            }
        }
    }
}
