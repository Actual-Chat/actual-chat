using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public static class AttachmentExt
{
    public static void ObserveUploadProgress(UploadProgressTracker progressTracker, Action<Func<Attachment, Attachment>> updateAttachment)
    {
        progressTracker.ProgressChanged += (_, value) => { UpdateProgress(value); };
        _ = progressTracker.Task.ContinueWith(UpdateResult, TaskScheduler.Default);
        if (progressTracker.Progress > 0)
            UpdateProgress(progressTracker.Progress);
        if (progressTracker.Task.IsCompleted)
            UpdateResult(progressTracker.Task);
        return;

        void UpdateProgress(double value)
        {
            updateAttachment(x => x with { Progress = (int)value });
        }

        void UpdateResult(Task<MediaContent> t)
        {
            if (t.IsCompletedSuccessfully) {
 #pragma warning disable VSTHRD002
                var mediaContent = t.Result;
 #pragma warning restore VSTHRD002
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
        }
    }
}
