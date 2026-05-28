using ActualLab.IO;

namespace ActualChat.Maui;

public static class LoadInPlaceResultExt
{
    extension(LoadInPlaceResult representation)
    {
        public FilePath Path => representation.FileUrl.Path!;

        public FilePath GetSuggestedFileName(NSItemProvider item)
        {
            FilePath fileName = item.SuggestedName.NullIfEmpty() ?? representation.Path.FileNameWithoutExtension;
            return !fileName.HasExtension ? fileName.ChangeExtension(representation.Path.Extension) : fileName;
        }

        public Task Copy(FilePath targetPath, CancellationToken cancellationToken = default)
            => representation.Path.CopyFile(targetPath, cancellationToken);
    }
}
