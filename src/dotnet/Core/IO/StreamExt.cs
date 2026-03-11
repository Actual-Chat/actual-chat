using ActualLab.IO;

namespace ActualChat.IO;

public static class StreamExt
{
    public static async Task CopyToFile(this Stream source, FilePath targetPath, CancellationToken cancellationToken = default)
    {
        var target = File.OpenWrite(targetPath);
        await using var _ = target.ConfigureAwait(false);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }
}
