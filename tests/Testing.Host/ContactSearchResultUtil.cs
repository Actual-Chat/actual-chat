using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Search;
using ActualChat.Users;
using ActualLab.Mathematics;
using Bunit.Extensions;

namespace ActualChat.Testing.Host;

public static class ContactSearchResultUtil
{
    public static List<FoundItem> BuildFoundContacts(
        this Account owner,
        IReadOnlyList<AccountFull> otherUsers,
        IReadOnlyList<Chat.Chat> publicChats,
        IReadOnlyList<Chat.Chat> privateChats)
    {
        var foundContacts = new List<FoundItem>();
        foundContacts.AddRange(otherUsers.Select(owner.BuildFoundContact));
        foundContacts.AddRange(publicChats.Select(owner.BuildFoundContact));
        foundContacts.AddRange(publicChats.Select(owner.BuildFoundContact));
        return foundContacts;
    }

    public static IEnumerable<FoundItem> BuildFoundContacts(this Account owner, params AccountFull[] others)
        => others.Select(owner.BuildFoundContact);

    public static IEnumerable<FoundItem> BuildFoundContacts(
        this Account owner,
        params Chat.Chat[] chats)
        => chats.Select(owner.BuildFoundContact);

    public static IEnumerable<FoundItem> BuildFoundContacts(
        this Account owner,
        params Place[] places)
        => places.Select(owner.BuildFoundContact);

    public static FoundItem BuildFoundContact(this Account owner, AccountFull other)
        => new (owner.BuildSearchResult(other), SearchScope.People);

    public static FoundItem BuildFoundContact(this Account owner, Chat.Chat chat)
        => new (owner.BuildSearchResult(chat), SearchScope.Groups);

    public static FoundItem BuildFoundContact(this Account owner, Place place)
        => new (owner.BuildSearchResult(place), SearchScope.Places);

    public static IEnumerable<ContactSearchResult> BuildSearchResults(this Account owner, params Chat.Chat[] chats)
        => chats.Select(x => owner.BuildSearchResult(x));

    public static IEnumerable<ContactSearchResult> BuildSearchResults(this Account owner, params Place[] places)
        => places.Select(x => owner.BuildSearchResult(x));

    public static IEnumerable<ContactSearchResult> BuildSearchResults(this Account owner, params (Chat.Chat Chat, Range<int>[]? SearchMatchPartRanges)[] chats)
        => chats.Select(x => owner.BuildSearchResult(x.Chat, x.SearchMatchPartRanges));

    public static IEnumerable<ContactSearchResult> BuildSearchResults(this Account owner, params (Place Place, Range<int>[]? SearchMatchPartRanges)[] chats)
        => chats.Select(x => owner.BuildSearchResult(x.Place, x.SearchMatchPartRanges));

    public static ContactSearchResult BuildSearchResult(this Account owner, Chat.Chat chat, Range<int>[]? searchMatchPartRanges = null)
        => chat.BuildSearchResult(owner.Id, searchMatchPartRanges);

    public static ContactSearchResult BuildSearchResult(this Account owner, Place place, Range<int>[]? searchMatchPartRanges = null)
        => place.BuildSearchResult(owner.Id, searchMatchPartRanges);

    public static ContactSearchResult BuildSearchResult(this Chat.Chat chat, UserId userId, Range<int>[]? searchMatchPartRanges = null)
        => BuildSearchResult(userId, chat.Id, chat.Title, searchMatchPartRanges);

    public static ContactSearchResult BuildSearchResult(this Place place, UserId userId, Range<int>[]? searchMatchPartRanges = null)
        => BuildSearchResult(userId, place.Id, place.Title, searchMatchPartRanges);

    public static ContactSearchResult BuildSearchResult(this UserId ownerId, ChatId chatId, string title, Range<int>[]? searchMatchPartRanges = null)
        => new (new ContactId(ownerId, chatId), searchMatchPartRanges.BuildSearchMatch(title));

    public static ContactSearchResult BuildSearchResult(this UserId ownerId, PlaceId placeId, string title, Range<int>[]? searchMatchPartRanges = null)
        => new (new ContactId(ownerId, placeId.ToRootChatId()), searchMatchPartRanges.BuildSearchMatch(title));

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
        if (searchMatchPartRanges.IsNullOrEmpty())
            return SearchMatch.New(fullName);

        var searchMatchParts = searchMatchPartRanges.Select(x => new SearchMatchPart(x, 1)).ToArray();
        return new SearchMatch(fullName, 1, searchMatchParts);
    }
}
