using ActualChat.Contacts;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Scoped UI service that builds and serves the per-chat pool of mentionable candidates
/// (users, chats, emojis). Replaces the legacy <c>MentionUI</c>.
/// Per-user candidates merge the caller's contacts with current-chat authors by <c>UserId</c>:
/// one entry per real user, primary name from the contact override (or account avatar name),
/// secondary name shown when the in-chat author name differs.
/// </summary>
public class MentionIndexUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;
    private IContacts Contacts => Hub.Contacts;
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();

    public async Task<MentionCandidate[]> Find(
        ChatId chatId,
        string filter,
        MentionKindFilter kindFilter,
        int limit,
        CancellationToken cancellationToken)
    {
        var pool = await GetPool(chatId, cancellationToken).ConfigureAwait(false);
        return MentionFilter.FilterAndRank(pool, filter, kindFilter, limit);
    }

    [ComputeMethod(AutoInvalidationDelay = 60)]
    protected virtual async Task<ApiArray<MentionCandidate>> GetPool(ChatId chatId, CancellationToken cancellationToken)
    {
        var users = await BuildUserCandidates(chatId, cancellationToken).ConfigureAwait(false);
        var chats = await BuildChatCandidates(cancellationToken).ConfigureAwait(false);
        var emojis = BuildEmojiCandidates();

        var result = new List<MentionCandidate>(users.Count + chats.Count + emojis.Count);
        result.AddRange(users);
        result.AddRange(chats);
        result.AddRange(emojis);
        return result.ToApiArray();
    }

    // Private methods

    private async Task<List<MentionCandidate>> BuildUserCandidates(ChatId chatId, CancellationToken cancellationToken)
    {
        var ownAccount = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        var ownUserId = ownAccount?.Id ?? default;

        var byUserId = new Dictionary<UserId, UserEntry>();

        var contactIds = await Contacts.ListIds(Session, null, cancellationToken).ConfigureAwait(false);
        var contactsLoad = await contactIds
            .Select(id => Contacts.Get(Session, id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        foreach (var contact in contactsLoad) {
            if (contact is null || contact.Kind != ContactKind.User || contact.State == ContactState.Blocked)
                continue;
            var userId = contact.UserId;
            if (userId is null || userId == ownUserId || userId.IsGuest)
                continue;

            var account = contact.Account;
            if (account is null)
                account = await Accounts.Get(Session, userId, cancellationToken).ConfigureAwait(false);
            if (account is null)
                continue;

            byUserId[userId] = new UserEntry(account, contact.PreferredPeerName, null, false);
        }

        var authorIds = await Authors.ListAuthorIds(Session, chatId, cancellationToken).ConfigureAwait(false);
        var authorsLoad = await authorIds
            .Select(id => Authors.Get(Session, chatId, id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        var accountsLoad = await authorIds
            .Select(id => Authors.GetAccount(Session, chatId, id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var authorsArr = authorsLoad.ToArray();
        var accountsArr = accountsLoad.ToArray();
        for (var i = 0; i < authorsArr.Length; i++) {
            var author = authorsArr[i];
            var account = accountsArr[i];
            if (author is null || author.IsAnonymous || account is null)
                continue;
            var userId = account.Id;
            if (userId == ownUserId || userId.IsGuest)
                continue;

            var authorName = author.Avatar.Name;
            byUserId[userId] = byUserId.TryGetValue(userId, out var existing)
                ? existing with { AuthorName = authorName, IsChatMember = true }
                : new UserEntry(account, null, authorName, true);
        }

        var result = new List<MentionCandidate>(byUserId.Count);
        foreach (var (userId, entry) in byUserId)
            result.Add(ToCandidate(userId, entry));
        return result;
    }

    private async Task<List<MentionCandidate>> BuildChatCandidates(CancellationToken cancellationToken)
    {
        var contactIds = await Contacts.ListIds(Session, null, cancellationToken).ConfigureAwait(false);
        var contactsLoad = await contactIds
            .Select(id => Contacts.Get(Session, id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<MentionCandidate>();
        foreach (var contact in contactsLoad) {
            if (contact is null || contact.Kind == ContactKind.User || contact.State == ContactState.Blocked)
                continue;
            var chat = contact.Chat;
            if (chat is null || chat.Title.IsNullOrEmpty())
                continue;

            result.Add(new MentionCandidate(
                MentionId.NewChat(chat.Id),
                MentionCandidateKind.Chat,
                chat.Title,
                null,
                null,
                MentionFilter.Tokenize(chat.Title)));
        }
        return result;
    }

    private static List<MentionCandidate> BuildEmojiCandidates()
    {
        var result = new List<MentionCandidate>(Emojis.All.Length);
        foreach (var emoji in Emojis.All) {
            var ref_ = EmojiRef.New(emoji);
            result.Add(new MentionCandidate(
                MentionId.NewEmoji(ref_),
                MentionCandidateKind.Emoji,
                emoji.Title,
                emoji.Symbol,
                null,
                MentionFilter.Tokenize(emoji.Title)));
        }
        return result;
    }

    private static MentionCandidate ToCandidate(UserId userId, UserEntry entry)
    {
        var account = entry.Account;
        var primary = entry.OverrideName.NullIfEmpty() ?? account.Avatar.Name;
        var authorName = entry.AuthorName;
        var secondary = !string.IsNullOrEmpty(authorName) && !string.Equals(authorName, primary, StringComparison.Ordinal)
            ? authorName
            : null;

        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in MentionFilter.Tokenize(primary))
            words.Add(w);
        if (secondary is not null) {
            foreach (var w in MentionFilter.Tokenize(secondary))
                words.Add(w);
        }

        return new MentionCandidate(
            MentionId.NewUser(userId),
            MentionCandidateKind.User,
            primary,
            secondary,
            account.Avatar.Picture,
            words.ToArray()
        ) {
            IsChatMember = entry.IsChatMember,
        };
    }

    // Nested types

    private sealed record UserEntry(Account Account, string? OverrideName, string? AuthorName, bool IsChatMember);
}
