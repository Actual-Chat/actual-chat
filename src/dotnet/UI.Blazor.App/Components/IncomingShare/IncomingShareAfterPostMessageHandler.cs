using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class IncomingShareAfterSendMessageHandler(AppUIHub hub) : IAfterSendMessageHandler
{
    private ToastUI ToastUI => hub.ToastUI;
    private ILogger Log => field ??= hub.LogFor<IncomingShareAfterSendMessageHandler>();

    public void Invoke(string args, Result<ChatEntry?> result)
    {
        var expectedUploadedFilesNumber = int.Parse(args, CultureInfo.InvariantCulture);

        TextEntryAttachment[]? attachments;
        if (result.HasError) {
            if (result.Error is OperationCanceledException)
                return;

            Log.LogError(result.Error, "Failed to post message for sharing");
            attachments = null;
        }
        else
            attachments = result.Value?.Attachments;

        if (attachments is null || attachments.Length == 0) {
            var info1 = $"Failed to share {expectedUploadedFilesNumber} files";
            ToastUI.Show(info1, "icon-alert-circle", ToastDismissDelay.Long);
            return;
        }

        int attachmentsLength = attachments.Length;
        var info = expectedUploadedFilesNumber == attachmentsLength
            ? attachmentsLength.ToInvariantString()
            : $"{attachmentsLength} of {expectedUploadedFilesNumber}";
        var isImage = attachments.All(c => c.IsSupportedImage());
        var isVideo = attachments.All(c => c.IsSupportedVideo());
        var fileText = isImage ? "image" : isVideo ? "video" : "file";
        if (expectedUploadedFilesNumber > 1)
            fileText += "s";
        info = info + " " + fileText + " shared";
        ToastUI.Show(info, "icon-checkmark-circle", ToastDismissDelay.Short);
    }
}
