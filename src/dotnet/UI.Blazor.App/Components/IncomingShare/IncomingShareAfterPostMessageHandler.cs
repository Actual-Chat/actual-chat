using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Resources;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Components;

public class IncomingShareAfterSendMessageHandler(AppUIHub hub) : IAfterSendMessageHandler
{
    private ToastUI ToastUI => hub.ToastUI;
    private IStringLocalizer L => hub.StringLocalizer;
    private ILogger Log => field ??= hub.LogFor<IncomingShareAfterSendMessageHandler>();

    public void Invoke(string args, Result<ChatEntry?> result)
    {
        var expectedUploadedFilesNumber = int.Parse(args);

        ChatEntryAttachment[]? attachments;
        if (result.HasError) {
            if (result.Error is OperationCanceledException)
                return;

            Log.LogError(result.Error, "Failed to post message for sharing");
            attachments = null;
        }
        else
            attachments = result.Value?.Attachments;

        if (attachments is null || attachments.Length == 0) {
            var failure = L.IncomingShare_Failed(expectedUploadedFilesNumber, expectedUploadedFilesNumber);
            ToastUI.Show(failure, "icon-alert-circle", ToastDismissDelay.Long);
            return;
        }

        var attachmentsLength = attachments.Length;
        var count = expectedUploadedFilesNumber == attachmentsLength
            ? attachmentsLength.ToString(CultureInfo.InvariantCulture)
            : L.IncomingShare_CountOfTotal_Format(attachmentsLength, expectedUploadedFilesNumber);
        var isImage = attachments.All(c => c.IsSupportedImage());
        var isVideo = attachments.All(c => c.IsSupportedVideo());
        var info = isImage
            ? L.IncomingShare_ImagesShared(expectedUploadedFilesNumber, count)
            : isVideo
                ? L.IncomingShare_VideosShared(expectedUploadedFilesNumber, count)
                : L.IncomingShare_FilesShared(expectedUploadedFilesNumber, count);
        ToastUI.Show(info, "icon-checkmark-circle", ToastDismissDelay.Short);
    }
}
