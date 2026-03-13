namespace ActualChat.Users;

public class ClaimMapper
{
    public const string NicknameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nickname";
    public const string SurnameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname";
    public const string GivenNameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname";
    public const string GithubNameClaim = "urn:github:name";

    public const string OwnNameClaim = $"urn:{Constants.Hosts.Voxt}:name";
    public const string OwnFullNameClaim = $"urn:{Constants.Hosts.Voxt}:fullname";

    public virtual AccountFull UpdateClaims(AccountFull account, Dictionary<string, string> httpClaims)
    {
        var (claims, name) = UpdateClaims(account.Claims, httpClaims);
        if (!name.IsNullOrEmpty())
            account = account with { Name = name };
        return account with { Claims = claims };
    }

    public virtual (ApiMap<string, string> Claims, string Name) UpdateClaims(
        ApiMap<string, string> claims, Dictionary<string, string> httpClaims)
    {
        var surname = Capitalize(httpClaims.GetValueOrDefault(SurnameClaim) ?? "").Trim();
        var givenName = Capitalize(httpClaims.GetValueOrDefault(GivenNameClaim) ?? "").Trim();
        var fullName = $"{givenName} {surname}".Trim();
        if (fullName.IsNullOrEmpty())
            fullName = (httpClaims.GetValueOrDefault(GithubNameClaim) ?? "").Trim();

        var name = (httpClaims.GetValueOrDefault(NicknameClaim) ?? "").Trim();
        if (name.IsNullOrEmpty())
            name = fullName;

        var newClaims = claims.Clone();
        if (!name.IsNullOrEmpty())
            newClaims.Add(OwnNameClaim, name);
        if (!fullName.IsNullOrEmpty())
            newClaims.Add(OwnFullNameClaim, name);

        return (newClaims, name);
    }

    private static string Capitalize(string s)
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        for (var i = 0; i < s.Length; i++) {
            var ch = i == 0
                ? char.ToUpper(s[i])
                : char.ToLower(s[i]);
            sb.Append(ch);
        }
        return sb.ToStringAndRelease();
    }
}
