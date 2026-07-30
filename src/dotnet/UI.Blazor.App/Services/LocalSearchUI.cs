using ActualChat.Contacts;
using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Scoped UI service for in-memory local search over the current user's contacts, chat
/// authors, places and emojis. Each <c>ListXxx</c> compute method returns its natural type;
/// <c>ListMentionCandidates</c> unions the per-category candidate lists into the mention
/// picker's pool, and <see cref="ListDefaultMentions"/> precomputes the empty-query view.
/// </summary>
public class LocalSearchUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    public const int DefaultViewLimit = 30;

    private static readonly ApiArray<MentionCandidate> EmojiCandidates =
        Emojis.All.Select(ToEmojiCandidate).ToApiArray();
    // Keeps the curated catalog order, so no TopByDefaultOrder here.
    private static readonly ApiArray<MentionCandidate> TopEmojiCandidates =
        EmojiCandidates.Take(DefaultViewLimit).ToApiArray();

    private IAuthors Authors => Hub.Authors;
    private IContacts Contacts => Hub.Contacts;
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private IPlaces Places => field ??= Services.GetRequiredService<IPlaces>();
    private RecentMentionsUI RecentMentionsUI => field ??= Services.GetRequiredService<RecentMentionsUI>();

    public async Task<FoundMention[]> FindMentions(
        ChatId? chatId,
        Func<MentionCandidate, bool> filter,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        // The empty-query views are served from the precomputed per-category top lists.
        if (query.IsNullOrEmpty()) {
            FoundMention[]? mentions = null;
            if (ReferenceEquals(filter, MentionCandidateFilters.All))
                mentions = await ListDefaultMentions(chatId, cancellationToken).ConfigureAwait(false);
            else if (ReferenceEquals(filter, MentionCandidateFilters.User))
                mentions = await BuildDefaultView(chatId, true, false, false, cancellationToken).ConfigureAwait(false);
            else if (ReferenceEquals(filter, MentionCandidateFilters.Chat))
                mentions = await BuildDefaultView(chatId, false, true, false, cancellationToken).ConfigureAwait(false);
            else if (ReferenceEquals(filter, MentionCandidateFilters.Emoji))
                mentions = await BuildDefaultView(chatId, false, false, true, cancellationToken).ConfigureAwait(false);
            if (mentions != null)
                return mentions.Length <= limit ? mentions : mentions[..limit];
        }

        var candidates = await ListMentionCandidates(chatId, cancellationToken).ConfigureAwait(false);
        var recencyScores = RecentMentionsUI.GetScores();
        candidates = candidates.FilterAndRank(filter, query, limit, recencyScores);
        var placeId = (chatId as PlaceChatId)?.PlaceId;
        return ToFoundMentions(candidates, query, placeId);
    }

    public async Task<IReadOnlyList<FoundContact>> FindContacts(
        SearchScope scope,
        string criteria,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
            return [];

        var candidates = scope switch {
            SearchScope.People => await ListUserContactCandidates(cancellationToken).ConfigureAwait(false),
            SearchScope.Groups => await ListChatContactCandidates(cancellationToken).ConfigureAwait(false),
            _ => [],
        };
        var query = new SearchQuery(criteria);
        return candidates
            .WithSearchMatchRank(query, c => c.Document)
            .FilterBySearchMatchRank()
            .OrderBySearchMatchRank()
            .Take(limit)
            .Select(x => new FoundContact(x.Source.Id, new SearchMatch(x.Source.Title, x.Rank, query)))
            .ToList();
    }

    [ComputeMethod(MinCacheDuration = 300)]
    public virtual async Task<FoundMention[]> ListDefaultMentions(ChatId? chatId, CancellationToken cancellationToken)
        => await BuildDefaultView(chatId, true, true, true, cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<ApiArray<MentionCandidate>> ListMentionCandidates(
        ChatId? chatId,
        CancellationToken cancellationToken)
    {
        var members = chatId is { } memberChatId
            ? await ListMemberCandidates(memberChatId, cancellationToken).ConfigureAwait(false)
            : ApiArray<MentionCandidate>.Empty;
        var users = await ListUserCandidates(cancellationToken).ConfigureAwait(false);
        var groups = await ListGroupCandidates(cancellationToken).ConfigureAwait(false);

        // Each chat member gets a single candidate; its contact-list duplicate is dropped.
        var memberUserIds = new HashSet<UserId>();
        foreach (var member in members) {
            if (member.Id.Target is UserId userId)
                memberUserIds.Add(userId);
        }

        var result = new List<MentionCandidate>(members.Count + users.Count + groups.Count + EmojiCandidates.Count);
        result.AddRange(members);
        foreach (var candidate in users) {
            if (candidate.Id.Target is UserId userId && memberUserIds.Contains(userId))
                continue;

            result.Add(candidate);
        }
        result.AddRange(groups);
        result.AddRange(EmojiCandidates);
        return result.ToApiArray();
    }

    [ComputeMethod(MinCacheDuration = 300)]
    public virtual async Task<ApiArray<MentionCandidate>> ListMemberCandidates(
        ChatId chatId, CancellationToken cancellationToken)
    {
        var ownUserId = await GetOwnUserId(cancellationToken).ConfigureAwait(false);
        var authors = await ListAuthors(chatId, cancellationToken).ConfigureAwait(false);
        var result = new List<MentionCandidate>();
        foreach (var (author, account) in authors) {
            if (account is null) {
                // Anonymous author — no account, so it gets an author-scoped mention.
                result.Add(ToAuthorCandidate(author));
                continue;
            }
            if (account.Id == ownUserId || account.IsGuest)
                continue;

            result.Add(ToMemberCandidate(author, account));
        }
        return result.ToApiArray();
    }

    [ComputeMethod(MinCacheDuration = 600)]
    public virtual async Task<ApiArray<MentionCandidate>> ListUserCandidates(CancellationToken cancellationToken)
    {
        var ownUserId = await GetOwnUserId(cancellationToken).ConfigureAwait(false);
        var users = await ListUsers(cancellationToken).ConfigureAwait(false);
        var result = new List<MentionCandidate>();
        foreach (var (contact, account) in users) {
            if (account.Id == ownUserId)
                continue;

            var name = contact.PreferredPeerName.NullIfEmpty() ?? account.Avatar.Name;
            result.Add(ToUserCandidate(account.Id, name, account, isChatMember: false));
        }
        return result.ToApiArray();
    }

    [ComputeMethod(MinCacheDuration = 600)]
    public virtual async Task<ApiArray<MentionCandidate>> ListGroupCandidates(CancellationToken cancellationToken)
    {
        var chats = await ListChats(cancellationToken).ConfigureAwait(false);
        var places = await ListPlaces(cancellationToken).ConfigureAwait(false);
        var placeChatLists = await places
            .Select(place => ListPlaceChats(place.Id, cancellationToken))
            .Collect(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);

        var result = new List<MentionCandidate>();
        foreach (var chat in chats)
            result.Add(ToChatCandidate(chat));
        for (var i = 0; i < places.Count; i++) {
            var place = places[i];
            result.Add(ToPlaceCandidate(place));
            foreach (var chat in placeChatLists[i])
                result.Add(ToPlaceChatCandidate(chat, place));
        }

        return result.ToApiArray();
    }

    [ComputeMethod(MinCacheDuration = 300)]
    public virtual async Task<IReadOnlyDictionary<ChatId, ContactInfo>> ListContactInfos(
        PlaceId? placeId, CancellationToken cancellationToken)
    {
        // Cached per placeId, so the ContactInfo instances - and their lazily built
        // SearchDocuments - are reused across keystrokes instead of rebuilt per query.
        var contacts = await ListContacts(placeId, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<ChatId, ContactInfo>(contacts.Count);
        foreach (var contact in contacts) {
            if (contact.Chat is not null)
                result.Add(contact.ChatId, new ContactInfo(contact));
        }

        return result;
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
    public virtual async Task<ApiArray<ContactCandidate>> ListUserContactCandidates(CancellationToken cancellationToken)
    {
        var ownUserId = await GetOwnUserId(cancellationToken).ConfigureAwait(false);
        var users = await ListUsers(cancellationToken).ConfigureAwait(false);
        var result = new List<ContactCandidate>();
        foreach (var (contact, account) in users) {
            if (account.Id == ownUserId)
                continue;

            var name = contact.PreferredPeerName.NullIfEmpty() ?? account.Avatar.Name;
            result.Add(new ContactCandidate(contact.Id, name, new SearchDocument(name, account.Avatar.Name)));
        }
        return result.ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<ContactCandidate>> ListChatContactCandidates(CancellationToken cancellationToken)
    {
        var ownUserId = await GetOwnUserId(cancellationToken).ConfigureAwait(false);
        if (ownUserId is not { } ownerId)
            return [];

        var chats = await ListChats(cancellationToken).ConfigureAwait(false);
        var result = new List<ContactCandidate>();
        foreach (var chat in chats) {
            if (chat.Id is not GroupChatId groupChatId)
                continue;

            var title = chat.Title;
            result.Add(new ContactCandidate(ContactId.NewChat(ownerId, groupChatId), title, new SearchDocument(title)));
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

    private async Task<FoundMention[]> BuildDefaultView(
        ChatId? chatId,
        bool withUsers,
        bool withGroups,
        bool withEmojis,
        CancellationToken cancellationToken)
    {
        // The empty-query view: recents first, then per-category tops merged in the default order.
        var members = withUsers && chatId is { } memberChatId
            ? await ListMemberCandidates(memberChatId, cancellationToken).ConfigureAwait(false)
            : ApiArray<MentionCandidate>.Empty;
        var users = withUsers
            ? await ListUserCandidates(cancellationToken).ConfigureAwait(false)
            : ApiArray<MentionCandidate>.Empty;
        var groups = withGroups
            ? await ListGroupCandidates(cancellationToken).ConfigureAwait(false)
            : ApiArray<MentionCandidate>.Empty;
        var emojis = withEmojis ? EmojiCandidates : ApiArray<MentionCandidate>.Empty;

        var recentMentions = await RecentMentionsUI.Settings.Use(cancellationToken).ConfigureAwait(false);
        var recencyScores = recentMentions.GetScores(Clocks.SystemClock.Now);
        var recents = new List<(MentionCandidate Candidate, double Score)>();
        if (recencyScores.Count != 0) {
            var seen = new HashSet<MentionRef>();
            foreach (var list in (ApiArray<MentionCandidate>[])[members, users, groups, emojis]) {
                foreach (var candidate in list) {
                    if (recencyScores.TryGetValue(candidate.Id, out var score) && seen.Add(candidate.Id))
                        recents.Add((candidate, score));
                }
            }
            recents.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        }

        var result = new List<MentionCandidate>(DefaultViewLimit);
        var used = new HashSet<MentionRef>();
        foreach (var (candidate, _) in recents) {
            if (result.Count >= DefaultViewLimit)
                break;

            if (used.Add(candidate.Id))
                result.Add(candidate);
        }
        foreach (var top in (IReadOnlyList<MentionCandidate>[])[
            members.TopByDefaultOrder(DefaultViewLimit),
            users.TopByDefaultOrder(DefaultViewLimit),
            groups.TopByDefaultOrder(DefaultViewLimit),
            withEmojis ? TopEmojiCandidates : ApiArray<MentionCandidate>.Empty,
        ]) {
            foreach (var candidate in top) {
                if (result.Count >= DefaultViewLimit)
                    break;

                if (used.Add(candidate.Id))
                    result.Add(candidate);
            }
        }

        var placeId = (chatId as PlaceChatId)?.PlaceId;
        return ToFoundMentions(result, "", placeId);
    }

    private static FoundMention[] ToFoundMentions(
        IReadOnlyList<MentionCandidate> candidates, string query, PlaceId? placeId)
    {
        var searchQuery = new SearchQuery(query);
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
            MentionRef.NewUser(userId),
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
            MentionRef.NewUser(account.Id),
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
            MentionRef.NewAuthor(author.Id),
            name,
            picture,
            new SearchDocument(name)) {
            IsChatMember = true,
        };
    }

    private static MentionCandidate ToChatCandidate(Chat.Chat chat)
        => new(
            MentionRef.NewChat(chat.Id),
            chat.Title,
            chat.Picture?.ToPicture() ?? new Picture(null, null, chat.Id.Value),
            new SearchDocument(chat.Title));

    private static MentionCandidate ToPlaceCandidate(Place place)
        => new(
            MentionRef.NewPlace(place.Id),
            place.Title,
            place.Picture?.ToPicture() ?? new Picture(null, null, place.Id.Value),
            new SearchDocument(place.Title)) {
            PlaceId = place.Id,
            PlaceTitle = place.Title,
        };

    private static MentionCandidate ToPlaceChatCandidate(Chat.Chat chat, Place place)
        => new(
            MentionRef.NewChat(chat.Id),
            chat.Title,
            chat.Picture?.ToPicture() ?? new Picture(null, null, chat.Id.Value),
            // Place name first so "fusion fun" matches "Funny Chat" in "Fusion Place".
            new SearchDocument(place.Title, chat.Title)) {
            PlaceId = place.Id,
            PlaceTitle = place.Title,
        };

    private static MentionCandidate ToEmojiCandidate(Emoji emoji)
        => new(
            MentionRef.NewEmoji(EmojiRef.New(emoji)),
            emoji.Title,
            null,
            new SearchDocument(emoji.Title));

    // Computes mention picker description suffix and the name baked into the mention on insert.
    private static (string? Description, string MentionName) Describe(MentionCandidate c, PlaceId? hostPlaceId)
    {
        switch (c.Id.Target) {
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

    // Nested types

    public sealed record ContactCandidate(ContactId Id, string Title, SearchDocument Document);
}
