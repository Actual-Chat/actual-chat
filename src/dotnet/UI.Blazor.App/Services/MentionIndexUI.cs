using ActualChat.Contacts;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Scoped UI service that builds and serves the per-chat pool of mentionable candidates
/// (users, authors, chats, place chats, emojis). Each <c>ListXxx</c> is a compute method,
/// so a change to any underlying contact / author / place re-runs only the affected list
/// and the <see cref="ListCandidates"/> union.
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
        var pool = await ListCandidates(chatId, cancellationToken).ConfigureAwait(false);
        return MentionFilter.FilterAndRank(pool, filter, kindFilter, limit);
    }

    [ComputeMethod]
    protected virtual async Task<ApiArray<MentionCandidate>> ListCandidates(
        ChatId chatId, CancellationToken cancellationToken)
    {
        var authors = await ListAuthors(chatId, cancellationToken).ConfigureAwait(false);
        var users = await ListUsers(cancellationToken).ConfigureAwait(false);
        var chats = await ListChats(cancellationToken).ConfigureAwait(false);
        var placeChats = await ListPlaceChats(cancellationToken).ConfigureAwait(false);
        var emojis = await ListEmojis(cancellationToken).ConfigureAwait(false);

        var result = new List<MentionCandidate>(
            authors.Count + users.Count + chats.Count + placeChats.Count + emojis.Count);

        // Authors (current-chat members) win over the same user from contacts.
        var memberUserIds = new HashSet<UserId>();
        foreach (var a in authors) {
            result.Add(a);
            if (a.Id.Target is UserId userId)
                memberUserIds.Add(userId);
        }
        foreach (var u in users) {
            if (u.Id.Target is UserId userId && memberUserIds.Contains(userId))
                continue;
            result.Add(u);
        }
        result.AddRange(chats);
        result.AddRange(placeChats);
        result.AddRange(emojis);
        return result.ToApiArray();
    }

    [ComputeMethod]
    protected virtual async Task<ApiArray<MentionCandidate>> ListUsers(CancellationToken cancellationToken)
    {
        var ownUserId = await GetOwnUserId(cancellationToken).ConfigureAwait(false);

        var contactIds = await Contacts.ListIds(Session, null, cancellationToken).ConfigureAwait(false);
        var contacts = await contactIds
            .Select(id => Contacts.Get(Session, id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<MentionCandidate>();
        foreach (var contact in contacts) {
            if (contact is null || contact.Kind != ContactKind.User || contact.State == ContactState.Blocked)
                continue;
            var userId = contact.UserId;
            if (userId is null || userId == ownUserId || userId.IsGuest)
                continue;

            var account = contact.Account
                ?? await Accounts.Get(Session, userId, cancellationToken).ConfigureAwait(false);
            if (account is null)
                continue;

            var name = contact.PreferredPeerName.NullIfEmpty() ?? account.Avatar.Name;
            result.Add(ToUserCandidate(userId, name, account, isChatMember: false));
        }
        return result.ToApiArray();
    }

    [ComputeMethod]
    protected virtual async Task<ApiArray<MentionCandidate>> ListAuthors(
        ChatId chatId, CancellationToken cancellationToken)
    {
        var ownUserId = await GetOwnUserId(cancellationToken).ConfigureAwait(false);

        var memberUserIds = await Authors.ListUserIds(Session, chatId, cancellationToken).ConfigureAwait(false);
        var accounts = await memberUserIds
            .Where(userId => userId != ownUserId && !userId.IsGuest)
            .Select(userId => Accounts.Get(Session, userId, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<MentionCandidate>();
        foreach (var account in accounts) {
            if (account is null)
                continue;
            result.Add(ToUserCandidate(account.Id, account.Avatar.Name, account, isChatMember: true));
        }
        return result.ToApiArray();
    }

    [ComputeMethod]
    protected virtual async Task<ApiArray<MentionCandidate>> ListChats(CancellationToken cancellationToken)
    {
        // Global (placeless) contacts: standalone group chats. Peer-chat contacts have
        // ContactKind.User and are handled by ListUsers; place chats by ListPlaceChats.
        var contactIds = await Contacts.ListIds(Session, null, cancellationToken).ConfigureAwait(false);
        var contacts = await contactIds
            .Select(id => Contacts.Get(Session, id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<MentionCandidate>();
        foreach (var contact in contacts) {
            if (contact is null || contact.Kind == ContactKind.User || contact.State == ContactState.Blocked)
                continue;
            var chat = contact.Chat;
            if (chat is null || chat.Title.IsNullOrEmpty() || chat.Id is PlaceChatId)
                continue;

            result.Add(new MentionCandidate(
                MentionId.NewChat(chat.Id),
                MentionCandidateKind.Chat,
                chat.Title,
                chat.Picture?.ToPicture() ?? new Picture(null, null, chat.Id.Value),
                MentionFilter.Normalize(chat.Title)));
        }
        return result.ToApiArray();
    }

    [ComputeMethod]
    protected virtual async Task<ApiArray<MentionCandidate>> ListPlaceChats(CancellationToken cancellationToken)
    {
        var placeIds = await Contacts.ListPlaceIds(Session, cancellationToken).ConfigureAwait(false);
        var result = new List<MentionCandidate>();
        foreach (var placeId in placeIds) {
            var perPlace = await ListPlaceChats(placeId, cancellationToken).ConfigureAwait(false);
            result.AddRange(perPlace);
        }
        return result.ToApiArray();
    }

    [ComputeMethod]
    protected virtual async Task<ApiArray<MentionCandidate>> ListPlaceChats(
        PlaceId placeId, CancellationToken cancellationToken)
    {
        var place = await Places.Get(Session, placeId, cancellationToken).ConfigureAwait(false);
        var placeTitle = place?.Title.NullIfEmpty();
        if (placeTitle is null)
            return ApiArray<MentionCandidate>.Empty;

        var result = new List<MentionCandidate>();

        // The place itself — surfaced as a PlaceMention so its link goes to the default chat.
        var placePicture = place!.Picture?.ToPicture() ?? new Picture(null, null, placeId.Value);
        result.Add(new MentionCandidate(
            MentionId.NewPlace(placeId),
            MentionCandidateKind.Chat,
            placeTitle,
            placePicture,
            MentionFilter.Normalize(placeTitle)) {
            PlaceId = placeId,
            PlaceTitle = placeTitle,
        });

        // The place's chats (root chat excluded — it IS the place, added above).
        var contactIds = await Contacts.ListIds(Session, placeId, cancellationToken).ConfigureAwait(false);
        var contacts = await contactIds
            .Select(id => Contacts.Get(Session, id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        foreach (var contact in contacts) {
            if (contact is null || contact.Kind == ContactKind.User || contact.State == ContactState.Blocked)
                continue;
            var chat = contact.Chat;
            if (chat is null || chat.Title.IsNullOrEmpty())
                continue;
            if (chat.Id is not PlaceChatId placeChatId || placeChatId.IsRoot)
                continue;

            result.Add(new MentionCandidate(
                MentionId.NewChat(chat.Id),
                MentionCandidateKind.Chat,
                chat.Title,
                chat.Picture?.ToPicture() ?? new Picture(null, null, chat.Id.Value),
                // Place name first so "fusion fun" matches "Funny Chat" in "Fusion Place".
                MentionFilter.Normalize(placeTitle, chat.Title)) {
                PlaceId = placeId,
                PlaceTitle = placeTitle,
            });
        }
        return result.ToApiArray();
    }

    [ComputeMethod]
    protected virtual Task<ApiArray<MentionCandidate>> ListEmojis(CancellationToken cancellationToken)
    {
        var result = new List<MentionCandidate>(Emojis.All.Length);
        foreach (var emoji in Emojis.All) {
            result.Add(new MentionCandidate(
                MentionId.NewEmoji(EmojiRef.New(emoji)),
                MentionCandidateKind.Emoji,
                emoji.Title,
                null,
                MentionFilter.Normalize(emoji.Title)));
        }
        return Task.FromResult(result.ToApiArray());
    }

    // Private methods

    private async Task<UserId?> GetOwnUserId(CancellationToken cancellationToken)
    {
        var ownAccount = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        return ownAccount?.Id;
    }

    private static MentionCandidate ToUserCandidate(UserId userId, string name, Account account, bool isChatMember)
    {
        var picture = account.Avatar.Picture
            ?? new Picture(null, null, DefaultUserPicture.GetAvatarKey(userId.Value));
        return new MentionCandidate(
            MentionId.NewUser(userId),
            MentionCandidateKind.User,
            name,
            picture,
            MentionFilter.Normalize(name)) {
            IsChatMember = isChatMember,
        };
    }
}
