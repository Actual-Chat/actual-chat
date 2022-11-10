using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Users;

public static class AccountExt
{
    public static bool IsActive([NotNullWhen(true)] this AccountFull? account)
        => AccountFull.MustBeActive.IsSatisfied(account);
}
