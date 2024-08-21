using Cysharp.Text;

namespace ActualChat.Users;

public class ClaimMapper
{
    public const string NicknameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nickname";
    public const string SurnameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname";
    public const string GivenNameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname";
    public const string GithubNameClaim = "urn:github:name";

    public const string OwnNameClaim = "urn:actual.chat:name";
    public const string OwnFullNameClaim = "urn:actual.chat:fullname";

    public virtual User UpdateClaims(User user, Dictionary<string, string> httpClaims)
    {
        var surname = Capitalize(httpClaims.GetValueOrDefault(SurnameClaim) ?? "").Trim();
        var givenName = Capitalize(httpClaims.GetValueOrDefault(GivenNameClaim) ?? "").Trim();
        var fullName = $"{givenName} {surname}".Trim();
        if (fullName.IsNullOrEmpty())
            fullName = (httpClaims.GetValueOrDefault(GithubNameClaim) ?? "").Trim();

        var name = (httpClaims.GetValueOrDefault(NicknameClaim) ?? "").Trim();
        if (name.IsNullOrEmpty())
            name = fullName;

        var newClaims = user.Claims.Clone();
        if (!name.IsNullOrEmpty()) {
            user = user with { Name = name };
            newClaims.Add(OwnNameClaim, name);
        }
        if (!fullName.IsNullOrEmpty())
            newClaims.Add(OwnFullNameClaim, name);

        return user with { Claims = newClaims };
    }

    private static string Capitalize(string s)
    {
        using var sb = ZString.CreateStringBuilder();
        for (var i = 0; i < s.Length; i++) {
            var ch = i == 0
                ? char.ToUpper(s[i], CultureInfo.InvariantCulture)
                : char.ToLower(s[i], CultureInfo.InvariantCulture);
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
