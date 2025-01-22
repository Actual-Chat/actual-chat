using ActualChat.MLSearch.Documents;
using ActualChat.Users;

namespace ActualChat.MLSearch;

public static class AccountExt
{
    public static IndexedUser ToIndexedUser(this AccountFull account, params ApiArray<PlaceId> placeIds)
        => new (account.Id) {
            Name = account.Name,
            PlaceIds = placeIds,
        };
}
