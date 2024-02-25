using ActualChat.Users;

namespace ActualChat.Search;

public static class AccountExt
{
    public static IndexedUserContact ToIndexedUserContact(this AccountFull account)
        => new() {
            Id = account.Id,
            FirstName = account.Name,
            SecondName = account.LastName,
            FullName = account.FullName,
        };
}
