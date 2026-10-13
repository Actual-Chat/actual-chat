using System.Text.RegularExpressions;

namespace ActualChat.Media;

/// <summary>
/// Recognizes the github.com links that have an API record behind them: a pull request, an issue,
/// a commit or a repository. Anything else on github.com - a file, a release, a user - is left to
/// the crawler.
/// </summary>
public static partial class GithubLinkParser
{
    // Top-level paths that are GitHub's own pages, not "owner/repository"
    private static readonly HashSet<string> ReservedOwners = new (StringComparer.OrdinalIgnoreCase) {
        "about", "account", "apps", "codespaces", "collections", "customer-stories", "dashboard",
        "enterprise", "explore", "features", "issues", "join", "login", "marketplace", "new",
        "notifications", "organizations", "orgs", "pricing", "pulls", "readme", "search", "security",
        "settings", "site", "sponsors", "topics", "trending",
    };

    public static GithubLinkTarget? TryParse(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !EgressHttpHandler.IsHttpUri(uri))
            return null;

        var host = uri.DnsSafeHost.ToLower();
        if (host is not ("github.com" or "www.github.com"))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return null;

        var owner = segments[0];
        var name = segments[1];
        if (ReservedOwners.Contains(owner) || !IsName(owner) || !IsName(name))
            return null;
        if (segments.Length == 2)
            return new GithubLinkTarget(GithubLinkKind.Repository, owner, name);
        if (segments.Length < 4)
            return null;

        var key = segments[3];
        return segments[2] switch {
            "pull" when IsNumber(key, out var number)
                => new GithubLinkTarget(GithubLinkKind.PullRequest, owner, name, number),
            "issues" when IsNumber(key, out var number)
                => new GithubLinkTarget(GithubLinkKind.Issue, owner, name, number),
            "commit" when ShaRegexFactory().IsMatch(key)
                => new GithubLinkTarget(GithubLinkKind.Commit, owner, name, Sha: key.ToLower()),
            _ => null,
        };
    }

    // Private methods

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9._])?$")]
    private static partial Regex NameRegexFactory();

    [GeneratedRegex(@"^[0-9a-fA-F]{7,40}$")]
    private static partial Regex ShaRegexFactory();

    private static bool IsName(string segment)
        => NameRegexFactory().IsMatch(segment);

    private static bool IsNumber(string segment, out int number)
        => int.TryParse(segment, out number) && number > 0;
}
