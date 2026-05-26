using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class MediaShareUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IMediaShareUI
{
    private IChats Chats => Hub.Chats;

    public async Task Share(ChatEntryAttachment attachment)
    {
        var sourceChatId = attachment.EntryId.ChatId;
        var modalModel = new ForwardMessageModal.Model(sourceChatId) {
            Title = "Share media",
            SubmitTitle = "Share",
        };
        await (await ModalUI.Show(modalModel).ConfigureAwait(true)).WhenClosed.ConfigureAwait(true);

        var selectedChatIds = modalModel.SelectedChatIds;
        if (selectedChatIds.Count == 0)
            return;

        foreach (var destinationChatId in selectedChatIds) {
            var cmd = new Chats_UpsertEntry(Session, destinationChatId, null) {
                Attachments = [
                    new ChatEntryAttachment {
                        MediaId = attachment.MediaId,
                        ThumbnailMediaId = attachment.ThumbnailMediaId,
                    },
                ],
            };
            await UICommander.Run(cmd, CancellationToken.None).ConfigureAwait(true);
        }

        var firstChatId = selectedChatIds.First();
        var info = await BuildInfoMessage().ConfigureAwait(true);
        ToastUI.Show(info, NavigateAction, "Navigate", ToastDismissDelay.Long);
        return;

        void NavigateAction()
            => _ = History.NavigateTo(Links.Chat(firstChatId));

        async Task<string> BuildInfoMessage()
        {
            var sb = ActualLab.Text.StringBuilderExt.Acquire();
            sb.Append("Shared media to ");
            Chat.Chat? chat = null;
            if (selectedChatIds.Count == 1)
                chat = await Chats.Get(Session, firstChatId, default).ConfigureAwait(true);
            if (chat != null) {
                sb.Append('\'');
                sb.Append(chat.Title);
                sb.Append("\' chat");
            }
            else {
                sb.Append(selectedChatIds.Count);
                sb.Append(" chats");
            }
            return sb.ToStringAndRelease();
        }
    }
}
