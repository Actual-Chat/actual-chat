using ActualLab.IO;

namespace ActualChat.UI.App.Services;

/// <summary>
/// Base video transcoder. Returns null (no transcoding) by default.
/// Platform-specific implementations (e.g., IosVideoTranscoder) override this.
/// </summary>
public class VideoTranscoder
{
    public virtual Task<FilePath?> TranscodeIfNeeded(
        FilePath sourceFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<FilePath?>(null);
}
