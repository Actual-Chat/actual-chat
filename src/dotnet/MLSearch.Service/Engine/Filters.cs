using ActualChat.Contacts;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Engine;

public interface IFilters
{
    ValueTask<IQueryFilter> Semantic(string text, CancellationToken cancellationToken = default);
    ValueTask<IQueryFilter> Keyword(string text, CancellationToken cancellationToken = default);
    ValueTask<IQueryFilter> Chat(Func<ChatSet, ChatSet> setBuilder, CancellationToken cancellationToken = default);
}

internal sealed class Filters(IContactsBackend contacts) : IFilters
{
    public ValueTask<IQueryFilter> Semantic(string text, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IQueryFilter>(new SemanticFilter<ChatSlice>(text));

    public ValueTask<IQueryFilter> Keyword(string text, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IQueryFilter>(new KeywordFilter<ChatSlice>(text.Split()));

    public async ValueTask<IQueryFilter> Chat(Func<ChatSet, ChatSet> setBuilder, CancellationToken cancellationToken = default)
    {
        var chatSet = setBuilder.Invoke(new EmptyChatSet());

        var builder = new ChatFilterBuilder(contacts);
        while (chatSet is not null) {
            await chatSet.Apply(builder, cancellationToken).ConfigureAwait(false);
            chatSet = chatSet.Next;
        }
        return builder.ChatFilter;
    }
}

internal sealed class ChatFilterBuilder(IContactsBackend contacts)
{
    public ChatFilter ChatFilter { get; } = new ();

    internal ValueTask IncludePublic(ContactSubset contactSubset, CancellationToken _)
    {
        if (!contactSubset.IsAll())
            ChatFilter.PlaceIds.Add(contactSubset.PlaceId);
        else
            ChatFilter.IncludePublic = true;
        return ValueTask.CompletedTask;
    }

    internal async ValueTask IncludePrivate(UserId userId, ContactSubset contactSubset, CancellationToken cancellationToken)
    {
        var privateContacts = await contacts.ListIdsForSearch(userId, contactSubset, false, cancellationToken).ConfigureAwait(false);
        ChatFilter.ChatIds.UnionWith(privateContacts.Select(c => c.ChatId));
    }

    internal ValueTask ExcludeChats(IEnumerable<ChatId> exclusions)
    {
        ChatFilter.ExcludedChatIds.UnionWith(exclusions);
        return ValueTask.CompletedTask;
    }
}

public abstract class ChatSet(ChatSet? next)
{
    public ChatSet Public() => Public(ContactSubset.All());
    public ChatSet Public(ContactSubset contactSubset) => new PublicChatSet(this, contactSubset);
    public ChatSet Private(UserId userId) => Private(userId, ContactSubset.All());
    public ChatSet Private(UserId userId, ContactSubset contactSubset) => new PrivateChatSet(this, userId, contactSubset);
    public ChatSet Exclude(IEnumerable<ChatId> exclusions) => new ExcludeChatSet(this, exclusions);

    internal ChatSet? Next => next;
    internal abstract ValueTask Apply(ChatFilterBuilder filterBuilder, CancellationToken cancellationToken = default);
}

internal sealed class EmptyChatSet() : ChatSet(null)
{
    internal override ValueTask Apply(ChatFilterBuilder filterBuilder, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}

internal sealed class PublicChatSet(ChatSet next, ContactSubset contactSubset) : ChatSet(next)
{
    internal override ValueTask Apply(ChatFilterBuilder filterBuilder, CancellationToken cancellationToken = default)
        => filterBuilder.IncludePublic(contactSubset, cancellationToken);
}

internal sealed class PrivateChatSet(ChatSet next, UserId userId, ContactSubset contactSubset) : ChatSet(next)
{
    internal override ValueTask Apply(ChatFilterBuilder filterBuilder, CancellationToken cancellationToken = default)
        => filterBuilder.IncludePrivate(userId, contactSubset, cancellationToken);
}

internal sealed class ExcludeChatSet(ChatSet next, IEnumerable<ChatId> exclusions) : ChatSet(next)
{
    internal override ValueTask Apply(ChatFilterBuilder filterBuilder, CancellationToken cancellationToken = default)
        => filterBuilder.ExcludeChats(exclusions);
}
