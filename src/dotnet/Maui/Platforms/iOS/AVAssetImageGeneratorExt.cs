using AVFoundation;

namespace ActualChat.Maui;

public static class AVAssetImageGeneratorExt
{
    public static Task<CGImage?> GenerateCGImage(this AVAssetImageGenerator generator, TimeSpan time)
    {
        var tcs = new AsyncTaskMethodBuilder<CGImage?>();
        generator.GenerateCGImageAsynchronously(time.ToCMTime(), (image, _, error) => {
            if (error is null)
                tcs.TrySetResult(image);
            else
                tcs.TrySetException(error.ToException());
        });
        return tcs.Task;
    }
}
