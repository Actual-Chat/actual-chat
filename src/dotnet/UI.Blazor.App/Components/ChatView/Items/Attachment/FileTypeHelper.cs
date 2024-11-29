namespace ActualChat.UI.Blazor.App.Components;

public class FileTypeHelper
{
    public static readonly Dictionary<FileType, string> FileClasses = new() {
        { FileType.Image, "file-image" },
        { FileType.Multimedia, "file-multimedia" },
        { FileType.Pdf, "file-pdf" },
        { FileType.Table, "file-table" },
        { FileType.Text, "file-text" },
        { FileType.Archive, "file-archive" },
    };

    public static readonly Dictionary<string, string> FileColors = new() {
        { "png", FileClasses[FileType.Image] },
        { "jpg", FileClasses[FileType.Image] },
        { "jpeg", FileClasses[FileType.Image] },
        { "bmp", FileClasses[FileType.Image] },
        { "gif", FileClasses[FileType.Image] },
        { "webp", FileClasses[FileType.Image] },
        { "mp3", FileClasses[FileType.Multimedia] },
        { "3gp", FileClasses[FileType.Multimedia] },
        { "avi", FileClasses[FileType.Multimedia] },
        { "mov", FileClasses[FileType.Multimedia] },
        { "m4v", FileClasses[FileType.Multimedia] },
        { "m4a", FileClasses[FileType.Multimedia] },
        { "mkv", FileClasses[FileType.Multimedia] },
        { "ogm", FileClasses[FileType.Multimedia] },
        { "ogg", FileClasses[FileType.Multimedia] },
        { "oga", FileClasses[FileType.Multimedia] },
        { "webm", FileClasses[FileType.Multimedia] },
        { "wav", FileClasses[FileType.Multimedia] },
        { "pdf", FileClasses[FileType.Pdf] },
        { "ppt", FileClasses[FileType.Pdf] },
        { "pptx", FileClasses[FileType.Pdf] },
        { "xls", FileClasses[FileType.Table] },
        { "xlsx", FileClasses[FileType.Table] },
        { "css", FileClasses[FileType.Table] },
        { "html", FileClasses[FileType.Table] },
        { "torrent", FileClasses[FileType.Table] },
        { "doc", FileClasses[FileType.Text] },
        { "txt", FileClasses[FileType.Text] },
        { "zip", FileClasses[FileType.Archive] },
        { "rar", FileClasses[FileType.Archive] },
    };
}
