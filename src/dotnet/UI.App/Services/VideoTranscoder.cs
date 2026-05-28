using ActualLab.IO;

namespace ActualChat.UI.App.Services;

/// <summary>
/// Base video transcoder. Returns null (no transcoding) by default.
/// Platform-specific implementations (e.g., AppleVideoTranscoder) override this.
/// </summary>
public class VideoTranscoder
{
    public Task<FilePath> Transcode(
        FilePath sourceFilePath,
        string mimeType,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        if (sourceFilePath.IsEmpty || !mimeType.StartsWith("video/"))
            return Task.FromResult(FilePath.Empty);

        return TranscodeInternal(sourceFilePath, progress, cancellationToken);
    }

    protected virtual Task<FilePath> TranscodeInternal(
        FilePath sourceFilePath,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
        => Task.FromResult(FilePath.Empty);
}
