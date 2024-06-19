using ActualChat.Chat.UI.Blazor.Components;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Search;
using ActualChat.Users;
using ActualLab.Mathematics;

namespace ActualChat.Testing.Host;

public static class ContactSearchResultUtil
{
    public static List<FoundContact> BuildFoundContacts(
        this Account owner,
        IReadOnlyList<AccountFull> otherUsers,
        IReadOnlyList<Chat.Chat> publicChats,
        IReadOnlyList<Chat.Chat> privateChats)
    {
        var foundContacts = new List<FoundContact>();
        foundContacts.AddRange(otherUsers.Select(owner.BuildFoundContact));
        foundContacts.AddRange(publicChats.Select(x => owner.BuildFoundContact(x, true)));
        foundContacts.AddRange(publicChats.Select(x => owner.BuildFoundContact(x, false)));
        return foundContacts;
    }

    public static IEnumerable<FoundContact> BuildFoundContacts(this Account owner, params AccountFull[] others)
        => others.Select(owner.BuildFoundContact);

    public static IEnumerable<FoundContact> BuildFoundContacts(
        this Account owner,
        bool isPublic,
        params Chat.Chat[] chats)
        => chats.Select(x => owner.BuildFoundContact(x, isPublic));

    public static FoundContact BuildFoundContact(this Account owner, AccountFull other)
        => new (owner.BuildSearchResult(other), ContactSearchScope.People);

    public static FoundContact BuildFoundContact(this Account owner, Chat.Chat chat, bool isPublic)
        => new (owner.BuildSearchResult(chat), isPublic ? ContactSearchScope.PublicChats : ContactSearchScope.PrivateChats);

    public static IEnumerable<ContactSearchResult> BuildSearchResults(this Account owner, params Chat.Chat[] chats)
        => chats.Select(x => owner.BuildSearchResult(x));

    public static IEnumerable<ContactSearchResult> BuildSearchResults(this Account owner, params (Chat.Chat Chat, Range<int>[]? SearchMatchPartRanges)[] chats)
        => chats.Select(x => owner.BuildSearchResult(x.Chat, x.SearchMatchPartRanges));

    public static ContactSearchResult BuildSearchResult(this Account owner, Chat.Chat chat, Range<int>[]? searchMatchPartRanges = null)
        => chat.BuildSearchResult(owner.Id, searchMatchPartRanges);

    public static ContactSearchResult BuildSearchResult(this Chat.Chat chat, UserId userId, Range<int>[]? searchMatchPartRanges = null)
        => BuildSearchResult(userId, chat.Id, chat.Title, searchMatchPartRanges);

    public static ContactSearchResult BuildSearchResult(this UserId ownerId, ChatId chatId, string title, Range<int>[]? searchMatchPartRanges = null)
        => new (new ContactId(ownerId, chatId), searchMatchPartRanges.BuildSearchMatch(title));

    public static IEnumerable<ContactSearchResult> BuildSearchResults(this Account owner, params AccountFull[] others)
        => others.Select(x => owner.BuildSearchResult(x));

    public static ContactSearchResult BuildSearchResult(this Account owner, AccountFull other, Range<int>[]? searchMatchPartRanges = null)
        => owner.Id.BuildSearchResult(other.Id, other.FullName, searchMatchPartRanges);

    public static ContactSearchResult BuildSearchResult(
        this UserId ownerId,
        UserId otherUserId,
        string title,
        Range<int>[]? searchMatchPartRanges = null)
        => new (new ContactId(ownerId, new PeerChatId(ownerId, otherUserId).ToChatId()),
            searchMatchPartRanges.BuildSearchMatch(title));

    public static SearchMatch BuildSearchMatch(this Range<int>[]? searchMatchPartRanges, string fullName)
    {
        var searchMatchParts = searchMatchPartRanges?.Select(x => new SearchMatchPart(x, 1)).ToArray();
        return searchMatchParts == null ? SearchMatch.New(fullName) : new SearchMatch(fullName, 1, searchMatchParts);
    }
}
