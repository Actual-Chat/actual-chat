using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch;

public static class AccountExt
{
    public static IndexedUser ToIndexedUser(this AccountFull account, params PlaceId[] placeIds)
        => new (account.Id) {
            Name = account.Name,
            PlaceIds = placeIds,
        };
}
