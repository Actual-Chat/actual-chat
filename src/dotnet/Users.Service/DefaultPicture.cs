using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Users;

public static class DefaultPicture
{
    public const int DefaultSize = 160;

    public static string Get(AccountFull account, int size = DefaultSize)
        => GetGravatar(account, size) ?? GetAvataaar(account.User.Name.GetMD5HashCode());

    public static string Get(AccountFull? account, string hash, int size = DefaultSize)
        => GetGravatar(account, size) ?? GetAvataaar(hash);

    public static string GetAvataaar(string hash)
        => $"https://avatars.dicebear.com/api/avataaars/{hash.UrlEncode()}.svg";

    public static string? GetGravatar(AccountFull? account, int size = DefaultSize)
        => GetGravatar(GetEmail(account), size);

    public static string? GetGravatar(string? email, int size = DefaultSize)
    {
        if (email.IsNullOrEmpty())
            return null;

        var hash = email.GetMD5HashCode().ToLowerInvariant();
        return $"https://www.gravatar.com/avatar/{hash}?s={size}";
    }

    private static string? GetEmail(AccountFull? account)
    {
        var emailClaim = account?.User.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email);
        var email = emailClaim?.Trim().NullIfEmpty();
        return email;
    }
}
