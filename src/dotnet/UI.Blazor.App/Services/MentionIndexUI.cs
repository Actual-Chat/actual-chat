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
    private IPlaces Places => field ??= Services.GetRequiredService<IPlaces>();

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

        // Current chat members — single source of truth matched by ChatMentionResolver.
        var memberUserIds = await Authors.ListUserIds(Session, chatId, cancellationToken).ConfigureAwait(false);
        var memberSet = memberUserIds.ToHashSet();

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

            byUserId[userId] = new UserEntry(account, contact.PreferredPeerName, null, memberSet.Contains(userId));
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
            var isMember = memberSet.Contains(userId);
            byUserId[userId] = byUserId.TryGetValue(userId, out var existing)
                ? existing with { AuthorName = authorName, IsChatMember = isMember }
                : new UserEntry(account, null, authorName, isMember);
        }

        var result = new List<MentionCandidate>(byUserId.Count);
        foreach (var (userId, entry) in byUserId)
            result.Add(ToCandidate(userId, entry));
        return result;
    }

    private async Task<List<MentionCandidate>> BuildChatCandidates(CancellationToken cancellationToken)
    {
        // Global contacts (group chats, peer chats, etc.) live under PlaceId == null.
        // Place-scoped contacts (the place's root + its public chats) live under each PlaceId.
        // Mirror ChatListUI.ListAllUnorderedRaw — iterate { null, ...places }.
        var placeIds = await Contacts.ListPlaceIds(Session, cancellationToken).ConfigureAwait(false);
        var scopes = new List<PlaceId?> { null };
        scopes.AddRange(placeIds);

        var placeTitleCache = new Dictionary<PlaceId, string?>();
        async ValueTask<string?> GetPlaceTitle(PlaceId placeId)
        {
            if (placeTitleCache.TryGetValue(placeId, out var cached))
                return cached;
            var place = await Places.Get(Session, placeId, cancellationToken).ConfigureAwait(false);
            var title = place?.Title.NullIfEmpty();
            placeTitleCache[placeId] = title;
            return title;
        }

        var result = new List<MentionCandidate>();
        var seen = new HashSet<MentionId>();

        // Surface each place itself as a PlaceMention candidate. The backend strips
        // place root chats from contact listings (ChatsBackend.GetPublicChatIdsFor),
        // so we need to add them explicitly using the user's known place ids.
        foreach (var placeId in placeIds) {
            var mentionId = MentionId.NewPlace(placeId);
            if (!seen.Add(mentionId))
                continue;
            var place = await Places.Get(Session, placeId, cancellationToken).ConfigureAwait(false);
            var placeTitle = place?.Title.NullIfEmpty();
            if (placeTitle is null)
                continue;
            placeTitleCache[placeId] = placeTitle;
            var picture = place?.Picture?.ToPicture() ?? new Picture(null, null, placeId.Value);
            result.Add(new MentionCandidate(
                mentionId,
                MentionCandidateKind.Chat,
                placeTitle,
                null,
                picture,
                MentionFilter.Tokenize(placeTitle)) {
                PlaceId = placeId,
                PlaceTitle = placeTitle,
            });
        }

        foreach (var scope in scopes) {
            var contactIds = await Contacts.ListIds(Session, scope, cancellationToken).ConfigureAwait(false);
            var contactsLoad = await contactIds
                .Select(id => Contacts.Get(Session, id, cancellationToken))
                .Collect(cancellationToken)
                .ConfigureAwait(false);

            foreach (var contact in contactsLoad) {
                if (contact is null || contact.Kind == ContactKind.User || contact.State == ContactState.Blocked)
                    continue;
                var chat = contact.Chat;
                if (chat is null || chat.Title.IsNullOrEmpty())
                    continue;

                var chatPicture = chat.Picture?.ToPicture()
                    ?? new Picture(null, null, chat.Id.Value);

                // A place's root chat (PlaceChatId.IsRoot) IS the place — surface as PlaceMention
                // so descriptions read "place" and the link goes to the place's default chat.
                if (chat.Id is PlaceChatId rootChatId && rootChatId.IsRoot) {
                    var placeId = rootChatId.PlaceId;
                    var placeTitle = await GetPlaceTitle(placeId).ConfigureAwait(false) ?? chat.Title;
                    var mentionId = MentionId.NewPlace(placeId);
                    if (!seen.Add(mentionId))
                        continue;
                    result.Add(new MentionCandidate(
                        mentionId,
                        MentionCandidateKind.Chat,
                        placeTitle,
                        null,
                        chatPicture,
                        MentionFilter.Tokenize(placeTitle)) {
                        PlaceId = placeId,
                        PlaceTitle = placeTitle,
                    });
                    continue;
                }

                string? placeTitleForSuffix = null;
                PlaceId? candidatePlaceId = null;
                if (chat.Id is PlaceChatId pc) {
                    candidatePlaceId = pc.PlaceId;
                    placeTitleForSuffix = await GetPlaceTitle(pc.PlaceId).ConfigureAwait(false);
                }

                var chatMentionId = MentionId.NewChat(chat.Id);
                if (!seen.Add(chatMentionId))
                    continue;
                result.Add(new MentionCandidate(
                    chatMentionId,
                    MentionCandidateKind.Chat,
                    chat.Title,
                    null,
                    chatPicture,
                    MentionFilter.Tokenize(chat.Title)) {
                    PlaceId = candidatePlaceId,
                    PlaceTitle = placeTitleForSuffix,
                });
            }
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

        var picture = account.Avatar.Picture
            ?? new Picture(null, null, DefaultUserPicture.GetAvatarKey(userId.Value));
        return new MentionCandidate(
            MentionId.NewUser(userId),
            MentionCandidateKind.User,
            primary,
            secondary,
            picture,
            words.ToArray()
        ) {
            IsChatMember = entry.IsChatMember,
        };
    }

    // Nested types

    private sealed record UserEntry(Account Account, string? OverrideName, string? AuthorName, bool IsChatMember);
}
