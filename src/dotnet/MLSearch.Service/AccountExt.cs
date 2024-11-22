using ActualChat.Search;
using ActualChat.Users;

namespace ActualChat.MLSearch;

public static class AccountExt
{
    public static IndexedUserContact ToIndexedUserContact(this AccountFull account, params PlaceId[] placeIds)
        => account.ToIndexedUserContact(placeIds.ToApiArray());

    public static IndexedUserContact ToIndexedUserContact(this AccountFull account, ApiArray<PlaceId> placeIds)
        => new() {
            Id = account.Id,
            FullName = account.Name,
            PlaceIds = placeIds,
        };
}
