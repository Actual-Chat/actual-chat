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
            var failure = expectedUploadedFilesNumber == 1
                ? L.IncomingShare_Failed_One(expectedUploadedFilesNumber)
                : L.IncomingShare_Failed_Other(expectedUploadedFilesNumber);
            ToastUI.Show(failure, "icon-alert-circle", ToastDismissDelay.Long);
            return;
        }

        var attachmentsLength = attachments.Length;
        var count = expectedUploadedFilesNumber == attachmentsLength
            ? attachmentsLength.ToString(CultureInfo.InvariantCulture)
            : L.IncomingShare_CountOfTotal_Format(attachmentsLength, expectedUploadedFilesNumber);
        var isImage = attachments.All(c => c.IsSupportedImage());
        var isVideo = attachments.All(c => c.IsSupportedVideo());
        var isOne = expectedUploadedFilesNumber == 1;
        var info = isImage
            ? isOne ? L.IncomingShare_ImagesShared_One(count) : L.IncomingShare_ImagesShared_Other(count)
            : isVideo
                ? isOne ? L.IncomingShare_VideosShared_One(count) : L.IncomingShare_VideosShared_Other(count)
                : isOne ? L.IncomingShare_FilesShared_One(count) : L.IncomingShare_FilesShared_Other(count);
        ToastUI.Show(info, "icon-checkmark-circle", ToastDismissDelay.Short);
    }
}
