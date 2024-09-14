
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

    internal ValueTask IncludePublic(PlaceId? placeId, CancellationToken _)
    {
        if (placeId.HasValue) {
            ChatFilter.PlaceIds.Add(placeId.Value);
        }
        else {
            ChatFilter.IncludePublic = true;
        }
        return ValueTask.CompletedTask;
    }

    internal async ValueTask IncludePrivate(UserId userId, PlaceId? placeId, CancellationToken cancellationToken)
    {
        var privateChatIds = await chats.GetPrivateChatIdsForUser(userId, placeId, cancellationToken).ConfigureAwait(false);
        ChatFilter.ChatIds.UnionWith(privateChatIds);
    }
}

public abstract class ChatSet(ChatSet? next)
{
    public ChatSet Public(PlaceId? placeId = default) => new PublicChatSet(this, placeId);
    public ChatSet Private(UserId userId, PlaceId? placeId = default) => new PrivateChatSet(this, userId, placeId);

    internal ChatSet? Next => next;
    internal abstract ValueTask Apply(ChatFilterBuilder filterBuilder, CancellationToken cancellationToken = default);
}

internal sealed class EmptyChatSet() : ChatSet(null)
{
    internal override ValueTask Apply(ChatFilterBuilder filterBuilder, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}

internal sealed class PublicChatSet(ChatSet next, PlaceId? placeId) : ChatSet(next)
{
    internal override ValueTask Apply(ChatFilterBuilder filterBuilder, CancellationToken cancellationToken = default)
        => filterBuilder.IncludePublic(placeId, cancellationToken);
}

internal sealed class PrivateChatSet(ChatSet next, UserId userId, PlaceId? placeId) : ChatSet(next)
{
    internal override ValueTask Apply(ChatFilterBuilder filterBuilder, CancellationToken cancellationToken = default)
        => filterBuilder.IncludePrivate(userId, placeId, cancellationToken);
}
