using System.Text;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Db;

/// <summary>
/// Transforms the claims from auth provider to the author properties. <br />
/// Default claim types can be found here
/// <seealso href="https://docs.microsoft.com/en-us/dotnet/api/system.security.claims.claimtypes" />
/// </summary>
public class ClaimMapper
{
    public virtual ValueTask Apply(
        UsersDbContext dbContext,
        DbUser dbUser,
        DbUserAuthor dbUserAuthor,
        IReadOnlyDictionary<string, string> claims,
        CancellationToken cancellationToken)
    {
        const string nameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nickname";
        const string githubNameClaim = "urn:github:name";
        const string surnameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname";
        const string firstnameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname";

        if (string.IsNullOrEmpty(dbUserAuthor.Nickname)
            && claims.TryGetValue(nameClaim, out var nickname)
            && !string.IsNullOrWhiteSpace(nickname)) {
            dbUserAuthor.Nickname = nickname;
        }

        claims.TryGetValue(surnameClaim, out var surname);
        claims.TryGetValue(firstnameClaim, out var firstname);

        var name = (Capitalize(firstname) ?? "") + " " + (Capitalize(surname) ?? "").Trim();

        if (string.IsNullOrWhiteSpace(name))
            claims.TryGetValue(githubNameClaim, out name);

        if (string.IsNullOrWhiteSpace(name)) {
            // ToDo: generate better unique names
            name = Invariant($"unnamed_{Guid.NewGuid():n}");
        }

        if (string.IsNullOrEmpty(dbUserAuthor.Nickname))
            dbUserAuthor.Nickname = name.Replace(" ", "", StringComparison.Ordinal);

        if (string.IsNullOrEmpty(dbUserAuthor.Name))
            dbUserAuthor.Name = name;

        static string? Capitalize(string? s)
        {
            if (s == null)
                return null;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++) {
                var ch = i == 0
                    ? char.ToUpper(s[i], CultureInfo.InvariantCulture)
                    : char.ToLower(s[i], CultureInfo.InvariantCulture);
                sb.Append(ch);
            }
            return sb.ToString();
        }
        // ToDo: read info (picture/name) from gravatar / github avatars
        return default;
    }
}
