using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public static class CopyChatToPlaceUI
{
    public static async Task CopyChat(
        ChatUIHub hub,
        ChatId sourceChatId,
        PlaceId placeId,
        Action? onBeforeCopy = null,
        Action<Exception?>? onAfterCopy = null,
        CancellationToken cancellationToken = default)
    {
        var session = hub.Session();
        var chat = await hub.Chats.Get(session, sourceChatId, default).Require();
        var place = await hub.Places.Get(session, placeId, cancellationToken).Require();
        var message = $"You are about to copy chat '{chat.Title}' to place '{place.Title}'. Do you want to proceed?";
        await hub.ModalUI.Show(new ConfirmModal.Model(false, message, () => _ = CopyInternal()), cancellationToken).ConfigureAwait(true);

        async Task CopyInternal() {
            onBeforeCopy?.Invoke();
            var correlationId = Guid.NewGuid().ToString();
            var command = new Chat_CopyChat(session, sourceChatId, placeId, correlationId);
            var (result, error) = await hub.UICommander().Run(command, cancellationToken).ConfigureAwait(true);
            onAfterCopy?.Invoke(error);
            if (error != null)
                return;

            if (!result.HasErrors) {
                var info = result.HasChanges
                    ? $"Chat '{chat.Title}' was successfully copied to place '{place.Title}'."
                    : $"Nothing has been done on coping chat '{chat.Title}' to place '{place.Title}'.";
                hub.ToastUI.Show(info, ToastDismissDelay.Long);
            } else {
                var model = new CopyChatToPlaceErrorModal.Model(correlationId, result.HasChanges, chat.Title, place.Title);
                await hub.ModalUI.Show(model, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static async Task PublishCopiedChat(
        ChatUIHub hub,
        ChatId newChatId,
        ChatId sourceChatId,
        CancellationToken cancellationToken = default)
    {
        var session = hub.Session();
        var newChat = await hub.Chats.Get(session, newChatId, default).Require();
        var place = await hub.Places.Get(session, newChatId.PlaceChatId.Require().PlaceId, default).Require();
        var message = $"You are about to publish copied chat '{newChat.Title}' from place '{place.Title}'. Do you want to proceed?";
        await hub.ModalUI.Show(new ConfirmModal.Model(false, message, () => _ = PublishInternal()), cancellationToken);

        async Task PublishInternal() {
            var command = new Chat_PublishCopiedChat(session, newChat.Id, sourceChatId);
            var (_, error) = await hub.UICommander().Run(command, cancellationToken).ConfigureAwait(true);
            if (error != null)
                return;
            hub.ToastUI.Show($"Chat '{newChat.Title}' was successfully published.", ToastDismissDelay.Long);
        }
    }
}
