using System.Collections.Frozen;
using ActualChat.Media;

namespace ActualChat.UI.Blazor.App.Components;

public static class FileTypeHelper
{
    private static readonly FrozenDictionary<string, FileType> ExtensionKinds = BuildExtensionKinds();

    private static readonly Dictionary<FileType, string> CssClasses = new() {
        { FileType.Text, "file-text" },
        { FileType.Table, "file-table" },
        { FileType.Presentation, "file-presentation" },
        { FileType.Pdf, "file-pdf" },
        { FileType.Archive, "file-archive" },
        { FileType.Code, "file-code" },
        { FileType.Audio, "file-audio" },
        { FileType.Executable, "file-executable" },
        { FileType.Image, "file-default" },
        { FileType.Video, "file-default" },
        { FileType.Default, "file-default" },
    };

    private static readonly Dictionary<FileType, string> IconClasses = new() {
        { FileType.Text, "icon-file-text" },
        { FileType.Table, "icon-file-sheet" },
        { FileType.Presentation, "icon-file-slides" },
        { FileType.Pdf, "icon-file-pdf" },
        { FileType.Archive, "icon-file-archive" },
        { FileType.Code, "icon-file-code" },
        { FileType.Audio, "icon-note" },
        { FileType.Executable, "icon-file-executable" },
        { FileType.Image, "icon-file-media" },
        { FileType.Video, "icon-play-fill" },
        { FileType.Default, "icon-file" },
    };

    public static FileType GetKind(string? fileName, string? contentType = null)
    {
        var ext = ResolveExtension(fileName, contentType);
        return ext.Length != 0 && ExtensionKinds.TryGetValue(ext, out var kind)
            ? kind
            : FileType.Default;
    }

    // Extension shown as a badge - empty when neither the file name nor the content type
    // yields something that actually looks like an extension (e.g. a numeric tail like
    // "235923765092375" from a name such as "12345.235923765092375").
    public static string GetDisplayExtension(string? fileName, string? contentType = null)
        => ResolveExtension(fileName, contentType);

    // Extension is the primary signal; the MIME content type is a fallback only when the
    // file name has none or an implausible one (browsers often send empty or octet-stream).
    private static string ResolveExtension(string? fileName, string? contentType)
    {
        var ext = fileName.IsNullOrEmpty() ? "" : ContentListPlumbing.FileExtension(fileName);
        if (IsPlausibleExtension(ext))
            return ext;
        if (!contentType.IsNullOrEmpty() && MediaMimeTypes.TryGetExtension(contentType, out var mimeExt)) {
            ext = mimeExt.TrimStart('.').ToLowerInvariant();
            if (IsPlausibleExtension(ext))
                return ext;
        }
        return "";
    }

    // Plausible = a known extension, or at most 8 alphanumeric chars with at least one letter.
    // Real extensions almost always contain a letter, so a long numeric tail is rejected.
    private static bool IsPlausibleExtension(string ext)
    {
        if (ext.Length == 0)
            return false;
        if (ExtensionKinds.ContainsKey(ext))
            return true;
        if (ext.Length > 8)
            return false;
        var hasLetter = false;
        foreach (var c in ext) {
            if (c is >= 'a' and <= 'z')
                hasLetter = true;
            else if (c is < '0' or > '9')
                return false;
        }
        return hasLetter;
    }

    public static string GetCssClass(FileType kind)
        => CssClasses.GetValueOrDefault(kind, "file-default");

    public static string GetIconClass(FileType kind)
        => IconClasses.GetValueOrDefault(kind, "icon-file");

    private static FrozenDictionary<string, FileType> BuildExtensionKinds()
    {
        var map = new Dictionary<string, FileType>(StringComparer.Ordinal);
        void Add(FileType kind, params string[] extensions) {
            foreach (var ext in extensions)
                map[ext] = kind;
        }
        Add(FileType.Text,
            "doc", "docx", "docm", "dot", "dotx", "dotm", "odt", "ott", "rtf",
            "txt", "md", "markdown", "tex", "pages", "wpd", "wps", "log",
            "epub", "mobi", "azw", "azw3");
        Add(FileType.Table,
            "xls", "xlsx", "xlsm", "xlsb", "xlt", "xltx", "xltm", "xlw",
            "ods", "ots", "csv", "tsv", "numbers");
        Add(FileType.Presentation,
            "ppt", "pptx", "pptm", "pps", "ppsx", "ppsm", "pot", "potx", "potm",
            "odp", "otp", "key");
        Add(FileType.Pdf,
            "pdf", "djvu", "xps", "oxps");
        Add(FileType.Archive,
            "zip", "rar", "7z", "tar", "gz", "gzip", "tgz", "bz2", "xz",
            "zst", "lz4", "lzma", "iso", "cab", "arj");
        Add(FileType.Code,
            "js", "mjs", "cjs", "ts", "tsx", "jsx", "json", "jsonld", "xml", "xaml",
            "html", "htm", "xhtml", "css", "scss", "sass", "less",
            "cs", "cshtml", "razor", "csproj", "sln", "props", "targets",
            "java", "kt", "kts", "swift", "py", "c", "cpp", "cc", "cxx", "h", "hh", "hpp",
            "go", "rs", "rb", "php", "bash", "zsh", "sql",
            "yml", "yaml", "toml", "ini", "conf", "env", "dockerfile",
            "dart", "vue", "sol", "pl", "lua", "r", "scala", "groovy", "gradle");
        Add(FileType.Audio,
            "mp3", "wav", "ogg", "oga", "opus", "flac", "m4a", "aac", "weba",
            "wma", "mid", "midi", "amr", "aiff", "aif", "ac3");
        Add(FileType.Executable,
            "exe", "msi", "apk", "appx", "msix", "dmg", "deb", "rpm", "appimage",
            "jar", "scr", "msc", "vbs", "wsf", "reg", "lnk",
            "bat", "cmd", "ps1", "sh", "com");
        Add(FileType.Image,
            "png", "jpg", "jpeg", "bmp", "gif", "webp", "svg", "tiff", "tif",
            "ico", "heic", "heif", "avif", "jfif");
        Add(FileType.Video,
            "mp4", "mov", "avi", "mkv", "webm", "m4v", "wmv", "3gp", "3g2",
            "mpg", "mpeg", "m2v", "ogv", "ogm", "flv", "mts");
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
