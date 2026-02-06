namespace ActualChat.UI.Blazor.App.Services;

public class IncomingShareSuggestions(IServiceProvider services)
{
    protected IServiceProvider Services { get; } = services;
    protected IChats Chats => field ??= Services.GetRequiredService<IChats>();
    protected Session Session => field ??= Services.GetRequiredService<Session>();
    protected ILogger Log => field ??= Services.LogFor(GetType());

    public void Push(ChatId chatId)
        => _ = Suggest(chatId);

    // TODO: throttling
    public async Task Suggest(ChatId chatId, CancellationToken cancellationToken = default)
    {
        try {
            var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
            if (chat is null)
                return;

            await SuggestInternal(chat, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to suggest incoming share to chat #{ChatId}", chatId);
        }
    }

    protected virtual Task SuggestInternal(Chat.Chat chat, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
