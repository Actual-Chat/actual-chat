using ActualChat.Contacts;
using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Scoped UI service for in-memory local search over the current user's contacts, chat
/// authors, places and emojis. Each <c>ListXxx</c> compute method returns its natural type;
/// the <c>ListMentionCandidates</c> overloads union them into the mention picker's pool.
/// </summary>
public class LocalSearchUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private IAuthors Authors => Hub.Authors;
    private IContacts Contacts => Hub.Contacts;
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private IPlaces Places => field ??= Services.GetRequiredService<IPlaces>();

    public async Task<FoundMention[]> FindMentions(
        ChatId? chatId,
        Func<MentionCandidate, bool> filter,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await ListMentionCandidates(chatId, cancellationToken).ConfigureAwait(false);
        candidates = candidates.FilterAndRank(filter, query, limit);

        var searchQuery = new SearchQuery(query);
        var placeId = (chatId as PlaceChatId)?.PlaceId;
        var result = new FoundMention[candidates.Count];
        for (var i = 0; i < candidates.Count; i++) {
            var c = candidates[i];
            var picture = c.Picture ?? new Picture(null, null, c.Title);
            var (description, mentionName) = Describe(c, placeId);
            result[i] = new FoundMention(c.Id, new SearchMatch(c.Title, searchQuery), picture) {
                IsChatMember = c.IsChatMember,
                Description = description,
                MentionName = mentionName,
            };
        }
        return result;
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<MentionCandidate>> ListMentionCandidates(
        ChatId? chatId,
        CancellationToken cancellationToken)
    {
        if (chatId is null)
            return await ListMentionCandidatesWithEmoji(cancellationToken).ConfigureAwait(false);

        var ownUserId = await GetOwnUserId(cancellationToken).ConfigureAwait(false);
        var authors = await ListAuthors(chatId, cancellationToken).ConfigureAwait(false);
        var rest = await ListMentionCandidatesWithEmoji(cancellationToken).ConfigureAwait(false);
        var result = new List<MentionCandidate>();

        // Each chat member gets a single candidate; its contact-list duplicate is dropped.
        var memberUserIds = new HashSet<UserId>();
        foreach (var (author, account) in authors) {
            if (account is null) {
                // Anonymous author — no account, so it gets an author-scoped mention.
                result.Add(ToAuthorCandidate(author));
                continue;
            }
            if (account.Id == ownUserId || account.IsGuest)
                continue;

            memberUserIds.Add(account.Id);
            result.Add(ToMemberCandidate(author, account));
        }
        foreach (var candidate in rest) {
            if (candidate.Id.TargetId is UserId userId && memberUserIds.Contains(userId))
                continue;

            result.Add(candidate);
        }
        return result.ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<MentionCandidate>> ListMentionCandidatesWithEmoji(
        CancellationToken cancellationToken)
    {
        var candidates = await ListMentionCandidates(cancellationToken).ConfigureAwait(false);
        var result = new List<MentionCandidate>(candidates);
        foreach (var emoji in Emojis.All)
            result.Add(ToEmojiCandidate(emoji));
        return result.ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<MentionCandidate>> ListMentionCandidates(CancellationToken cancellationToken)
    {
        var ownUserId = await GetOwnUserId(cancellationToken).ConfigureAwait(false);
        var users = await ListUsers(cancellationToken).ConfigureAwait(false);
        var chats = await ListChats(cancellationToken).ConfigureAwait(false);
        var places = await ListPlaces(cancellationToken).ConfigureAwait(false);
        var result = new List<MentionCandidate>();

        // Users
        foreach (var (contact, account) in users) {
            if (account.Id == ownUserId)
                continue;

            var name = contact.PreferredPeerName.NullIfEmpty() ?? account.Avatar.Name;
            result.Add(ToUserCandidate(account.Id, name, account, isChatMember: false));
        }

        // Chats
        foreach (var chat in chats)
            result.Add(ToChatCandidate(chat));

        // Place chats
        foreach (var place in places) {
            result.Add(ToPlaceCandidate(place));
            var placeChats = await ListPlaceChats(place.Id, cancellationToken).ConfigureAwait(false);
            foreach (var chat in placeChats)
                result.Add(ToPlaceChatCandidate(chat, place));
        }

        return result.ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<Contact>> ListContacts(
        PlaceId? placeId, CancellationToken cancellationToken)
    {
        var contactIds = await Contacts.ListIds(Session, placeId, cancellationToken).ConfigureAwait(false);
        var contacts = await contactIds
            .Select(id => Contacts.Get(Session, id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<Contact>();
        foreach (var contact in contacts) {
            if (contact is not null)
                result.Add(contact);
        }
        return result.ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<(Contact Contact, Account Account)>> ListUsers(
        CancellationToken cancellationToken)
    {
        var contacts = await ListContacts(null, cancellationToken).ConfigureAwait(false);
        var result = new List<(Contact Contact, Account Account)>();
        foreach (var contact in contacts) {
            if (contact.Kind != ContactKind.User || contact.State == ContactState.Blocked)
                continue;
            var userId = contact.UserId;
            if (userId is null || userId.IsGuest)
                continue;
            var account = contact.Account
                ?? await Accounts.Get(Session, userId, cancellationToken).ConfigureAwait(false);
            if (account is null)
                continue;
            result.Add((contact, account));
        }
        return result.ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<(Author Author, Account? Account)>> ListAuthors(
        ChatId chatId, CancellationToken cancellationToken)
    {
        var authorIds = await Authors.ListAuthorIds(Session, chatId, cancellationToken).ConfigureAwait(false);
        var authors = await authorIds
            .Select(id => Authors.Get(Session, chatId, id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<(Author Author, Account? Account)>();
        foreach (var author in authors) {
            if (author is null || author.HasLeft)
                continue;
            var account = author.IsAnonymous
                ? null
                : await Authors.GetAccount(Session, chatId, author.Id, cancellationToken).ConfigureAwait(false);
            result.Add((author, account));
        }
        return result.ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<Chat.Chat>> ListChats(CancellationToken cancellationToken)
    {
        var contacts = await ListContacts(null, cancellationToken).ConfigureAwait(false);
        var result = new List<Chat.Chat>();
        foreach (var contact in contacts) {
            if (contact.Kind != ContactKind.Chat || contact.State == ContactState.Blocked)
                continue;
            var chat = contact.Chat;
            if (chat.Title.IsNullOrEmpty() || chat.Id is PlaceChatId)
                continue;
            result.Add(chat);
        }
        return result.ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<Place>> ListPlaces(CancellationToken cancellationToken)
    {
        var placeIds = await Contacts.ListPlaceIds(Session, cancellationToken).ConfigureAwait(false);
        var places = await placeIds
            .Select(id => Places.Get(Session, id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<Place>();
        foreach (var place in places) {
            if (place is null || place.Title.IsNullOrEmpty())
                continue;
            result.Add(place);
        }
        return result.ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<Chat.Chat>> ListPlaceChats(
        PlaceId placeId, CancellationToken cancellationToken)
    {
        var contacts = await ListContacts(placeId, cancellationToken).ConfigureAwait(false);
        var result = new List<Chat.Chat>();
        foreach (var contact in contacts) {
            if (contact.Kind == ContactKind.User || contact.State == ContactState.Blocked)
                continue;
            var chat = contact.Chat;
            if (chat.Title.IsNullOrEmpty())
                continue;
            // The root chat is the place itself; ListPlaces surfaces it separately.
            if (chat.Id is not PlaceChatId placeChatId || placeChatId.IsRoot)
                continue;
            result.Add(chat);
        }
        return result.ToApiArray();
    }

    // Private methods

    private async Task<UserId?> GetOwnUserId(CancellationToken cancellationToken)
    {
        var ownAccount = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        return ownAccount.Id;
    }

    private static MentionCandidate ToUserCandidate(UserId userId, string name, Account account, bool isChatMember)
    {
        var picture = account.Avatar.Picture
            ?? new Picture(null, null, DefaultUserPicture.GetAvatarKey(userId.Value));
        return new MentionCandidate(
            MentionId.NewUser(userId),
            name,
            picture,
            new SearchDocument(name)) {
            IsChatMember = isChatMember,
        };
    }

    private static MentionCandidate ToMemberCandidate(Author author, Account account)
    {
        // author.Avatar.Name is the chat-effective name — it already reflects a contact rename
        // or a chat-specific avatar. When it differs from the account name, show both:
        // "MyBestie (Magnolia)"; otherwise just the account name.
        var userName = account.Avatar.Name;
        var name = author.Avatar.Name;
        var title = name != userName ? $"{name} ({userName})" : userName;
        var picture = account.Avatar.Picture
            ?? new Picture(null, null, DefaultUserPicture.GetAvatarKey(account.Id.Value));
        return new MentionCandidate(
            MentionId.NewUser(account.Id),
            title,
            picture,
            new SearchDocument(name, userName)) {
            IsChatMember = true,
        };
    }

    private static MentionCandidate ToAuthorCandidate(Author author)
    {
        var name = author.Avatar.Name;
        var picture = author.Avatar.Picture
            ?? new Picture(null, null, DefaultUserPicture.GetAvatarKey(author.Id.Value));
        return new MentionCandidate(
            MentionId.NewAuthor(author.Id),
            name,
            picture,
            new SearchDocument(name)) {
            IsChatMember = true,
        };
    }

    private static MentionCandidate ToChatCandidate(Chat.Chat chat)
        => new(
            MentionId.NewChat(chat.Id),
            chat.Title,
            chat.Picture?.ToPicture() ?? new Picture(null, null, chat.Id.Value),
            new SearchDocument(chat.Title));

    private static MentionCandidate ToPlaceCandidate(Place place)
        => new(
            MentionId.NewPlace(place.Id),
            place.Title,
            place.Picture?.ToPicture() ?? new Picture(null, null, place.Id.Value),
            new SearchDocument(place.Title)) {
            PlaceId = place.Id,
            PlaceTitle = place.Title,
        };

    private static MentionCandidate ToPlaceChatCandidate(Chat.Chat chat, Place place)
        => new(
            MentionId.NewChat(chat.Id),
            chat.Title,
            chat.Picture?.ToPicture() ?? new Picture(null, null, chat.Id.Value),
            // Place name first so "fusion fun" matches "Funny Chat" in "Fusion Place".
            new SearchDocument(place.Title, chat.Title)) {
            PlaceId = place.Id,
            PlaceTitle = place.Title,
        };

    private static MentionCandidate ToEmojiCandidate(Emoji emoji)
        => new(
            MentionId.NewEmoji(EmojiRef.New(emoji)),
            emoji.Title,
            null,
            new SearchDocument(emoji.Title));

    // Computes mention picker description suffix and the name baked into the mention on insert.
    private static (string? Description, string MentionName) Describe(MentionCandidate c, PlaceId? hostPlaceId)
    {
        switch (c.Id.TargetId) {
        case EmojiRef:
            return ("emoji", c.Title);
        case PlaceId:
            return ("place", c.Title);
        case UserId or AuthorId:
            return (c.IsChatMember ? null : "- not in this chat", c.Title);
        case ChatId:
            if (c.PlaceId is not { } placeId)
                return (null, c.Title); // standalone group chat
            if (placeId == hostPlaceId)
                return ("in this place", c.Title);
            var placeTitle = c.PlaceTitle.NullIfEmpty();
            return placeTitle is null
                ? ("in another place", c.Title)
                : ($"in {placeTitle}", $"{placeTitle} › {c.Title}");
        default:
            return (null, c.Title);
        }
    }
}
