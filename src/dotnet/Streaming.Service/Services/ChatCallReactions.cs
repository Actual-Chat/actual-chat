using ActualChat.Live;

namespace ActualChat.Streaming.Services;

public class ChatCallReactions(IServiceProvider services) : IChatCallReactions
{
    private IServiceProvider Services { get; } = services;
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private IChatCallReactionsBackend Backend => field ??= Services.GetRequiredService<IChatCallReactionsBackend>();

    // [ComputeMethod]
    public virtual async Task<ApiArray<CallReaction>> List(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return await Backend.List(chatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task Send(Session session, ChatId chatId, Emoji emoji, CancellationToken cancellationToken)
    {
        if (!CallReaction.AllowedEmojis.Contains(emoji))
            throw StandardError.Constraint("This emoji can't be sent as a call reaction.");

        // GetOwn returns null for a non-member, which gates reactions to the chat's authors.
        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author is null)
            return;

        await Backend.Send(chatId, author.Id, emoji, cancellationToken).ConfigureAwait(false);
    }
}
