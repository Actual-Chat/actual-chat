using ActualLab.IO;

namespace ActualChat.UI.Services;

public sealed class FileUploadSource(FilePath filePath)
    : StreamUploadSource(() => Task.FromResult<Stream>(File.OpenRead(filePath)))
{
    public FilePath FilePath => filePath;

    public override string ToString()
        => FilePath;
}
