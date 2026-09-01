using ActualChat.Live;

namespace ActualChat.Streaming.Services;

public class ChatTypingActivities(IServiceProvider services) : IChatTypingActivities
{
    private IServiceProvider Services { get; } = services;
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private IChatTypingActivitiesBackend Backend
        => field ??= Services.GetRequiredService<IChatTypingActivitiesBackend>();

    // [ComputeMethod]
    public virtual async Task<ApiArray<AuthorId>> ListTypingAuthorIds(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return await Backend.ListTypingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task SetTyping(
        Session session,
        ChatId chatId,
        TypingActivityKind kind,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        // GetOwn returns null for a non-member, which gates typing to authors that can post here.
        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return;

        await Backend.SetTyping(chatId, author.Id, kind, ttl, cancellationToken).ConfigureAwait(false);
    }
}
