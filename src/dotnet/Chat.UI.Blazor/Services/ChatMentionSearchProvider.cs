using ActualChat.Search;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatMentionSearchProvider : ISearchProvider<MentionSearchResult>
{
    private Session Session { get; }
    private IChats Chats { get; }

    public string ChatId { get; }

    public ChatMentionSearchProvider(IServiceProvider services, string chatId)
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
