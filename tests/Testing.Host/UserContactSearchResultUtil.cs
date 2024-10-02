using ActualChat.Chat;
using ActualChat.Search;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;
using ActualLab.Mathematics;
using Bunit.Extensions;

namespace ActualChat.Testing.Host;

public static class UserContactSearchResultUtil
{
    public static IEnumerable<FoundItem> BuildFoundContacts(this Account owner, params AccountFull[] others)
        => others.Select(owner.BuildFoundContact);

    public static FoundItem BuildFoundContact(this Account owner, AccountFull other)
        => new (owner.BuildSearchResult(other), SearchScope.People);

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
}
