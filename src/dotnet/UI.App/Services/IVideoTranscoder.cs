using ActualLab.IO;

namespace ActualChat.UI.App.Services;

public interface IVideoTranscoder
{
    Task<VideoTranscodeResult?> TranscodeIfNeeded(
        FilePath sourceFilePath,
        string mimeType,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

/// No-op for platforms without client-side transcoding (web, Android, Windows).
public class NullVideoTranscoder : IVideoTranscoder
{
    public Task<VideoTranscodeResult?> TranscodeIfNeeded(
        FilePath sourceFilePath, string mimeType,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<VideoTranscodeResult?>(null);
}

public record VideoTranscodeResult(FilePath FilePath, string ContentType, long Length);
