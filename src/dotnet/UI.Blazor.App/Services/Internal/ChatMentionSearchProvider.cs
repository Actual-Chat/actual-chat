using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services.Internal;

internal class ChatMentionSearchProvider(IServiceProvider services, ChatId chatId)
    : ISearchProvider<MentionSearchResult>
{
    private Session Session { get; } = services.Session();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();

    public ChatId ChatId { get; } = chatId;

    public async Task<MentionSearchResult[]> Find(string filter, int limit, CancellationToken cancellationToken)
    {
        var searchPhrase = filter[..Math.Min(64, filter.Length)].ToSearchPhrase(true, true);
        var authors = await Chats.ListMentionableAuthors(Session, ChatId, cancellationToken).ConfigureAwait(false);
        var accounts = await authors
            .Select(a => Authors.GetAccount(Session, ChatId, a.Id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        // Default scheduler is used from here

        var mentions = authors
            .Zip(accounts)
            .Select(pair => {
                var (author, account) = pair;
                var best = searchPhrase.GetMatch(author.Avatar.Name);
                if (account is { Avatar.Name: { } accountName } && accountName != author.Avatar.Name) {
                    var accountMatch = searchPhrase.GetMatch(accountName);
                    if (accountMatch.Rank > best.Rank)
                        best = accountMatch;
                }
                return new { author, searchMatch = best };
            })
            .Where(x => x.searchMatch.Rank > 0 || searchPhrase.IsEmpty)
            .OrderByDescending(x => x.searchMatch.Rank)
            .ThenBy(x => x.author.Avatar.Name)
            .Select(x => new MentionSearchResult(MentionId.NewAuthor(x.author.Id), x.searchMatch, x.author.Avatar.Picture!))
            .Take(limit)
            .ToArray();
        return mentions;
    }
}
