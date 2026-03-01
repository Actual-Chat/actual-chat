using ActualLab.IO;

namespace ActualChat.UI.App.Services;

public interface IVideoTranscoder
{
    Task<FilePath?> TranscodeIfNeeded(
        FilePath sourceFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

/// No-op for platforms without client-side transcoding (web, Android, Windows).
public class NullVideoTranscoder : IVideoTranscoder
{
    public Task<FilePath?> TranscodeIfNeeded(
        FilePath sourceFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<FilePath?>(null);
}
