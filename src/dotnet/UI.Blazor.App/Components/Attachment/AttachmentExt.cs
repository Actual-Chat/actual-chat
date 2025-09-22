using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public static class AttachmentExt
{
    public static void ObserveUploadProgress(UploadSessionProgressTracker progressTracker, Action<Func<Attachment, Attachment>> updateAttachment)
    {
        progressTracker.ProgressChanged += (_, value) => {
            updateAttachment(x => x with { Progress = (int)value });
        };
        _ = progressTracker.Task.ContinueWith(t => {
            if (t.IsCompletedSuccessfully) {
                var mediaContent = t.Result;
                updateAttachment(x => x with {
                    MediaId = mediaContent.MediaId,
                    ThumbnailMediaId = mediaContent.ThumbnailMediaId,
                });
            }
            else if (t.IsFaulted) {
                updateAttachment(x => x with {
                    Failed = true,
                });
            }
        }, TaskScheduler.Default);
    }
}
