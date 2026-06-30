using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class IncomingShareUI(AppUIHub hub)
{
    private AppUIHub Hub { get; } = hub;
    private Session Session => Hub.Session;
    private IChats Chats => Hub.Chats;
    private ModalUI ModalUI => Hub.ModalUI;
    private History History => Hub.History;
    private ToastUI ToastUI => Hub.ToastUI;
    private SendingMessages SendingMessages => Hub.SendingMessages;
    private SentContentStorage SentContentStorage
        => field ??= Hub.Services.GetRequiredService<SentContentStorage>();
    private FilePreviews FilePreviews
        => field ??= Hub.Services.GetRequiredService<FilePreviews>();
    private ILogger Log { get; } = StaticLog.For<IncomingShareUI>();

    public void ShareText(string plainText, ChatId? targetChatId = null)
    {
        _ = ShareInternal();
        return;

        async ValueTask ShareInternal() {
            if (targetChatId != null) {
                var targetChat = await Chats.Get(Session, targetChatId, Hub.StopToken).ConfigureAwait(true);
                if (targetChat is { }) {
                    await PrefillChatEditor(targetChatId, plainText, []).ConfigureAwait(true);
                    return;
                }
            }

            await ShowShareModal(new IncomingShareModal.Model(plainText)).ConfigureAwait(true);
        }
    }

    public void ShareFiles(AttachFileInfo[] files, ChatId? targetChatId = null)
    {
        _ = ShareInternal();
        return;

        async ValueTask ShareInternal() {
            if (targetChatId != null) {
                var targetChat = await Chats.Get(Session, targetChatId, Hub.StopToken).ConfigureAwait(true);
                if (targetChat is { }) {
                    await PrefillChatEditor(targetChatId, "", files).ConfigureAwait(true);
                    return;
                }
            }

            await ShowShareModal(new IncomingShareModal.Model(files)).ConfigureAwait(true);
        }
    }

    private async Task ShowShareModal(IncomingShareModal.Model model)
    {
        var modalRef = await ModalUI.Show(model).ConfigureAwait(true);
        await modalRef.WhenClosed.ConfigureAwait(true);
        if (!model.IsConfirmed || model.SelectedChatIds.Count == 0)
            return;

        if (model.HasFiles)
            await SendFiles(model.SelectedChatIds, model.Files, model.Comment).ConfigureAwait(true);
        else
            await SendText(model.SelectedChatIds, model.Text, model.Comment).ConfigureAwait(true);
    }

    private async Task SendText(IReadOnlyCollection<ChatId> chatIds, string text, string comment)
    {
        CancellationToken cancellationToken = default;
        foreach (var chatId in chatIds) {
            if (!comment.IsNullOrEmpty())
                await SendingMessages.Send(SendMessageRequest.NewMessage(chatId, comment), cancellationToken).ConfigureAwait(true);
            await SendingMessages.Send(SendMessageRequest.NewMessage(chatId, text), cancellationToken).ConfigureAwait(true);
        }
        await History.NavigateTo(Links.Chat(chatIds.First())).ConfigureAwait(true);
    }

    private async Task SendFiles(IReadOnlyCollection<ChatId> chatIds, AttachFileInfo[] files, string comment)
    {
        if (chatIds.Count == 1 && files.Length <= Constants.Attachments.FileCountLimit) {
            await PrefillChatEditor(chatIds.First(), comment, files).ConfigureAwait(true);
            return;
        }

        try {
            var postTasks = new List<(Task, ChatId, int)>();
            var uploadHandles = new List<FilesUploadHandle>();
            var commentToSend = comment;
            var mediaScope = chatIds.Count == 1 ? chatIds.First().Value : "";
            foreach (var filesChunk in files.Chunk(Constants.Attachments.FileCountLimit)) {
                var attachments = await CreateAttachmentList(filesChunk).ConfigureAwait(true);
                var uploads = (await SendingMessages.Upload(attachments, mediaScope).ConfigureAwait(true))!;
                uploadHandles.Add(uploads);
                foreach (var chatId in chatIds) {
#pragma warning disable CA2025
                    var postTask = PostMessage(chatId, commentToSend, uploads, attachments.Length);
#pragma warning restore CA2025
                    postTasks.Add((postTask, chatId, filesChunk.Length));
                }
                commentToSend = "";
            }
            try {
                await Task.WhenAll(postTasks.Select(c => c.Item1)).ConfigureAwait(true);
            }
            finally {
                // Dispose only after the posts have run: an earlier release deletes the upload sessions
                // out from under the in-flight posts, which then arrive empty and are rejected.
                foreach (var uploads in uploadHandles)
                    uploads.Dispose();
            }
            var failedTasks = postTasks
                .Where(c => !c.Item1.IsCompletedSuccessfully)
                .ToArray();
            if (failedTasks.Length == 0)
                return;

            var failedToUploadFilesNumber = failedTasks.Sum(c => c.Item3);
            ToastUI.Show($"Failed to share {failedToUploadFilesNumber} files", "icon-alert-circle", ToastDismissDelay.Long);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to share files");
            ToastUI.Show("Failed to share files", "icon-alert-circle", ToastDismissDelay.Long);
        }
    }

    private async Task PrefillChatEditor(ChatId chatId, string text, AttachFileInfo[] files)
    {
        SentContentStorage.Push(chatId, files, text);
        await History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
    }

    private async Task<ImmutableArray<Attachment>> CreateAttachmentList(IEnumerable<AttachFileInfo> fileInfos)
    {
        Exception? fileSizeLimitError = null;
        var attachments = new List<Attachment>();
        foreach (var fileInfo in fileInfos) {
            var fileProvider = fileInfo.FileProvider;
            var fileMetadata = fileProvider.Metadata;
            if (fileMetadata.Length > Constants.Attachments.FileSizeLimit) {
                Log.LogWarning("File size limit exceeded for '{Url}'. Actual size is {FileSize}", fileMetadata.FileName, fileMetadata.Length);
                fileSizeLimitError ??= AttachmentList.FileTooBigError();
                continue;
            }
            var preview = await FilePreviews.Get(fileProvider, fileMetadata.FileType, Hub.StopToken).ConfigureAwait(true);
            var attachment = new SourceAttachment(
                fileMetadata.FileName,
                fileMetadata.FileType,
                fileMetadata.Length,
                preview) {
                FileProvider = fileProvider,
            };
            attachment.Cleanups.Add(AttachmentCleanupFactory.ForFile(fileProvider));
            attachments.Add(attachment);
        }
        if (fileSizeLimitError != null)
            Hub.Services.UICommander().ShowError(fileSizeLimitError);
        return [..attachments];
    }

    private async Task PostMessage(ChatId chatId, string text, FilesUploadHandle uploads, int attachmentCount)
    {
        var afterHandler = new AfterSendMessageHandler(
            SendingMessages.AfterSendMessageHandlerKeys.IncomingShare,
            attachmentCount.ToString());
        var cmd = SendMessageRequest.NewMessage(chatId, text, uploads, afterHandler);
        var postTask = await SendingMessages.Send(cmd, default).ConfigureAwait(false);
        await postTask.SilentAwait();
    }
}
