using ActualLab.IO;

namespace ActualChat.UI.App.Services;

/// <summary>
/// Base video transcoder. Returns null (no transcoding) by default.
/// Platform-specific implementations (e.g., IosVideoTranscoder) override this.
/// </summary>
public class VideoTranscoder
{
    public Task<FilePath> TranscodeIfNeeded(
        FilePath sourceFilePath,
        string mimeType,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (sourceFilePath.IsEmpty || !mimeType.OrdinalStartsWith("video/"))
            return Task.FromResult(FilePath.Empty);

        return TranscodeIfNeededInternal(sourceFilePath, progress, cancellationToken);
    }

    protected virtual Task<FilePath> TranscodeIfNeededInternal(
        FilePath sourceFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(FilePath.Empty);
}
