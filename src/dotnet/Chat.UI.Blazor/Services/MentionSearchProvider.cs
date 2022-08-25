using ActualChat.Search;

namespace ActualChat.Chat.UI.Blazor.Services;

public class MentionSearchProvider : ISearchProvider<MentionSearchResult>
{
    private readonly Session _session;
    private readonly string _chatId;
    private readonly IChats _chats;

    public MentionSearchProvider(Session session, string chatId, IChats chats)
    {
        _session = session;
        _chatId = chatId;
        _chats = chats;
    }

    public async Task<MentionSearchResult[]> Find(string filter, int limit, CancellationToken cancellationToken)
    {
        var searchPhrase = filter[..Math.Min(64, filter.Length)].ToSearchPhrase(true, true);
        var authors = await _chats.ListMentionableAuthors(_session, _chatId, cancellationToken).ConfigureAwait(false);
        var mentions = (
            from author in authors
            let searchMatch = searchPhrase.GetMatch(author.Name)
            where searchMatch.Rank > 0 || searchPhrase.IsEmpty
            orderby searchMatch.Rank descending, author.Name
            select new MentionSearchResult($"a:{author.Id}", searchMatch, author.Picture)
            ).Take(limit)
            .ToArray();
        return mentions;
    }
}
