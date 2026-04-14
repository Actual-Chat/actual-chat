using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

public class MentionUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;

    public async Task<MentionSearchResult[]> Find(
        ChatId chatId, string filter, int limit, CancellationToken cancellationToken)
    {
        var searchPhrase = filter[..Math.Min(64, filter.Length)].ToSearchPhrase(true, true);
        var candidates = await ListMentionCandidates(chatId, cancellationToken).ConfigureAwait(false);
        // Default scheduler is used from here

        return candidates
            .Select(c => new { c.Author, searchMatch = GetBestMatch(searchPhrase, c) })
            .Where(x => x.searchMatch.Rank > 0 || searchPhrase.IsEmpty)
            .OrderByDescending(x => x.searchMatch.Rank)
            .ThenBy(x => x.Author.Avatar.Name)
            .Select(x => new MentionSearchResult(MentionId.NewAuthor(x.Author.Id), x.searchMatch, x.Author.Avatar.Picture!))
            .Take(limit)
            .ToArray();
    }

    [ComputeMethod(AutoInvalidationDelay = 60_000)]
    protected virtual async Task<ApiArray<MentionCandidate>> ListMentionCandidates(
        ChatId chatId, CancellationToken cancellationToken)
    {
        var authors = await Chats.ListMentionableAuthors(Session, chatId, cancellationToken).ConfigureAwait(false);
        var accounts = await authors
            .Select(a => Authors.GetAccount(Session, chatId, a.Id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return authors
            .Zip(accounts)
            .Where(pair => pair.Second is not null)
            .Select(pair => new MentionCandidate(pair.First, pair.Second!.Avatar.Name))
            .ToApiArray();
    }

    private static SearchMatch GetBestMatch(SearchPhrase searchPhrase, MentionCandidate candidate)
    {
        var best = searchPhrase.GetMatch(candidate.Author.Avatar.Name);
        if (candidate.AccountAvatarName == candidate.Author.Avatar.Name)
            return best;

        var accountMatch = searchPhrase.GetMatch(candidate.AccountAvatarName);
        return accountMatch.Rank > best.Rank ? accountMatch : best;
    }

    protected sealed record MentionCandidate(Author Author, string AccountAvatarName);
}
