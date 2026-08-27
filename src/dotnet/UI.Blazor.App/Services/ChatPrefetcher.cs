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
        // The optional second argument is the entry lid a /chat/{id}?n={lid} link opens at
        if (arguments.Length is < 1 or > 2 || !ChatId.TryParse(arguments[0], out var chatId))
            return Task.CompletedTask;

        var entryLid = 0L;
        if (arguments.Length == 2 && !long.TryParse(arguments[1], out entryLid))
            return Task.CompletedTask;

        return ChatUI.Prefetch(chatId, entryLid, cancellationToken);
    }
}
