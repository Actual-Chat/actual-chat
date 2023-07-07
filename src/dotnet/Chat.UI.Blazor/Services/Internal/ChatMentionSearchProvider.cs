using ActualChat.Search;

namespace ActualChat.Chat.UI.Blazor.Services.Internal;

internal class ChatMentionSearchProvider : ISearchProvider<MentionSearchResult>
{
    private Session Session { get; }
    private IChats Chats { get; }

    public ChatId ChatId { get; }

    public ChatMentionSearchProvider(IServiceProvider services, ChatId chatId)
    {
        Session = services.GetRequiredService<Session>();
        Chats = services.GetRequiredService<IChats>();
        ChatId = chatId;
    }

    public async Task<MentionSearchResult[]> Find(string filter, int limit, CancellationToken cancellationToken)
    {
        var searchPhrase = filter[..Math.Min(64, filter.Length)].ToSearchPhrase(true, true);
        var authors = await Chats.ListMentionableAuthors(Session, ChatId, cancellationToken).ConfigureAwait(false);
        // Default scheduler is used from here

        var mentions = authors
            .Select(author => new { author, searchMatch = searchPhrase.GetMatch(author.Avatar.Name) })
            .Where(x => x.searchMatch.Rank > 0 || searchPhrase.IsEmpty)
            .OrderByDescending(x => x.searchMatch.Rank)
            .ThenBy(x => x.author.Avatar.Name, StringComparer.Ordinal)
            .Select(x => new MentionSearchResult(new MentionId(x.author.Id, AssumeValid.Option), x.searchMatch, x.author.Avatar.Picture!))
            .Take(limit)
            .ToArray();
        return mentions;
    }
}
