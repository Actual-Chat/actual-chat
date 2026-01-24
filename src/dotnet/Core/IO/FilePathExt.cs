using ActualChat.Hashing;
using ActualLab.Generators;
using ActualLab.IO;

namespace ActualChat.IO;

public static class FilePathExt
{
    extension(FilePath path)
    {
        public bool IsSubPathOf(FilePath baseBath)
        {
            var relativePath = path.RelativeTo(baseBath);
            var pathValue = relativePath.Value;
            return !OrdinalEquals(pathValue, ".")
                && !OrdinalEquals(pathValue, "..")
                && !pathValue.OrdinalStartsWith("../")
                && !pathValue.OrdinalStartsWith(@"..\")
                && !relativePath.IsRooted;
        }

        public FilePath RequireFileExists([CallerArgumentExpression(nameof(path))] string name = "")
        {
            if (!File.Exists(path))
                throw StandardError.NotFound<FilePath>($"{name} '{path}' does not exist.");
            return path;
        }

        public FilePath RequireHash(string expectedHash, bool trim = true, [CallerArgumentExpression(nameof(path))] string name = "")
        {
            var text = File.ReadAllText(path);
            if (trim)
                text = text.Trim();
            var hash = text.Hash().SHA256().AlphaNumeric();
            if (!OrdinalEquals(hash, expectedHash))
                throw StandardError.Configuration($"{name} ('{path}'): hash '{hash}' does not match '{expectedHash}'.");

            return path;
        }

        public FilePath ToUnique(int randomLength = 5)
            => path.DirectoryPath | $"{path.FileNameWithoutExtension}-{RandomStringGenerator.Default.Next(randomLength)}.{path.Extension}";
    }
}
