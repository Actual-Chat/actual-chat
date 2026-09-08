using AVFoundation;

namespace ActualChat.Maui;

public static class AVAssetImageGeneratorExt
{
    public static Task<CGImage?> GenerateCGImage(
        this AVAssetImageGenerator generator,
        TimeSpan time,
        CancellationToken cancellationToken = default)
    {
        var tcs = new AsyncTaskMethodBuilder<CGImage?>();
        var ctr = cancellationToken.Register(() => {
            generator.CancelAllCGImageGeneration();
            tcs.TrySetCanceled(cancellationToken);
        });
        generator.GenerateCGImageAsynchronously(time.ToCMTime(), (image, _, error) => {
            if (error is null)
                tcs.TrySetResult(image);
            else
                tcs.TrySetException(error.ToException());
            ctr.DisposeSilently();
        });
        return tcs.Task;
    }
}
