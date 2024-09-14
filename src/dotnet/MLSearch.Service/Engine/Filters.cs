
using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Engine;

public interface IFilters
{
    ValueTask<IQueryFilter> Semantic(string text, CancellationToken cancellationToken = default);
    ValueTask<IQueryFilter> Keyword(string text, CancellationToken cancellationToken = default);
    ValueTask<IQueryFilter> Chat(Func<ChatSet, ChatSet> setBuilder, CancellationToken cancellationToken = default);
}

internal sealed class Filters(IChatsBackend chats) : IFilters
{
    public ValueTask<IQueryFilter> Semantic(string text, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IQueryFilter>(new SemanticFilter<ChatSlice>(text));

    public ValueTask<IQueryFilter> Keyword(string text, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IQueryFilter>(new KeywordFilter<ChatSlice>(text.Split()));

    public async ValueTask<IQueryFilter> Chat(Func<ChatSet, ChatSet> setBuilder, CancellationToken cancellationToken = default)
    {
        var chatSet = setBuilder.Invoke(new EmptyChatSet());

        var builder = new ChatFilterBuilder(chats);
        while (chatSet is not null) {
            await chatSet.Apply(builder, cancellationToken).ConfigureAwait(false);
            chatSet = chatSet.Next;
        }
        return builder.ChatFilter;
    }
}

internal sealed class ChatFilterBuilder(IChatsBackend chats)
{
    public ChatFilter ChatFilter { get; } = new ChatFilter();

    internal ValueTask IncludePublic(CancellationToken _)
    {
        ChatFilter.IncludePublic = true;
        return ValueTask.CompletedTask;
    }

    internal async ValueTask IncludePrivate(UserId userId, CancellationToken cancellationToken)
    {
        var privateChatIds = await chats.GetPrivateChatIdsForUser(userId, cancellationToken).ConfigureAwait(false);
        ChatFilter.ChatIds.UnionWith(privateChatIds);
    }
}

public abstract class ChatSet(ChatSet? next)
{
    public ChatSet Private(UserId ownerAuthorUserId) => new PrivateChatSet(this, ownerAuthorUserId);
    public ChatSet Public() => new PublicChatSet(this);

    internal ChatSet? Next => next;
    internal abstract ValueTask Apply(ChatFilterBuilder filterBuilder, CancellationToken cancellationToken = default);
}

internal sealed class EmptyChatSet() : ChatSet(null)
{
    internal override ValueTask Apply(ChatFilterBuilder filterBuilder, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}

internal sealed class PublicChatSet(ChatSet next) : ChatSet(next)
{
    internal override ValueTask Apply(ChatFilterBuilder filterBuilder, CancellationToken cancellationToken = default)
        => filterBuilder.IncludePublic(cancellationToken);
}

internal sealed class PrivateChatSet(ChatSet next, UserId userId) : ChatSet(next)
{
    internal override ValueTask Apply(ChatFilterBuilder filterBuilder, CancellationToken cancellationToken = default)
        => filterBuilder.IncludePrivate(userId, cancellationToken);
}
