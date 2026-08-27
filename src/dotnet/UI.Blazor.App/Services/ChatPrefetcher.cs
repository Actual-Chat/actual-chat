using ActualChat.UI.Blazor.Components;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// The <see cref="IPrefetcher"/> behind <c>data-prefetch</c> on anything that opens a chat.
/// Its single argument is the chat id.
/// </summary>
public sealed class ChatPrefetcher(AppUIHub hub) : IPrefetcher
{
    private ChatUI ChatUI => hub.ChatUI;
    public Task Prefetch(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments.Length != 1 || !ChatId.TryParse(arguments[0], out var chatId))
            return Task.CompletedTask;

        return ChatUI.Prefetch(chatId, cancellationToken);
    }
}
