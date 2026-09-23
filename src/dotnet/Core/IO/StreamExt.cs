using System.Buffers;
using ActualLab.IO;

namespace ActualChat.IO;

public static class StreamExt
{
    private const int BufferSize = 64 * 1024;

    public static Task CopyToFile(this Stream source, FilePath targetPath, CancellationToken cancellationToken = default)
        => source.CopyToFile(targetPath, null, null, cancellationToken);

    // `length` is what progress is measured against: without it there's nothing to report
    public static async Task CopyToFile(
        this Stream source,
        FilePath targetPath,
        long? length,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        var target = File.OpenWrite(targetPath);
        await using var _ = target.ConfigureAwait(false);
        if (progress == null || length is not > 0) {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try {
            var copied = 0L;
            var lastPercent = -1;
            while (true) {
                var readLength = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (readLength == 0)
                    return;

                await target.WriteAsync(buffer.AsMemory(0, readLength), cancellationToken).ConfigureAwait(false);
                copied += readLength;
                // Clamped: a source may outgrow the length it declared, and 100+ reaches the UI
                var percent = (int)Math.Min(100, 100 * copied / length.GetValueOrDefault());
                if (percent == lastPercent)
                    continue; // A report per whole percent - the consumer is typically a render

                lastPercent = percent;
                progress.Report(percent);
            }
        }
        finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
