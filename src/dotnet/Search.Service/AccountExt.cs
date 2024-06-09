using ActualChat.Users;

namespace ActualChat.Search;

public static class AccountExt
{
    public static IndexedUserContact ToIndexedUserContact(this AccountFull account, params PlaceId[] placeIds)
        => account.ToIndexedUserContact(placeIds.ToApiArray());

    public static IndexedUserContact ToIndexedUserContact(this AccountFull account, ApiArray<PlaceId> placeIds)
        => new() {
            Id = account.Id,
            FirstName = account.Name,
            SecondName = account.LastName,
            FullName = account.FullName,
            PlaceIds = placeIds,
        };
}
