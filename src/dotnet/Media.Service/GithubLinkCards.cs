using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ActualChat.Media.Module;

namespace ActualChat.Media;

/// <summary>
/// Builds the preview of a GitHub link from what api.github.com returns for it, instead of the page's
/// Open Graph card, whose renderer rate-limits per node (#5171). A link the API can't serve - a
/// private repository the token doesn't cover, a rate limit, an outage - gives null, and the link
/// gets the ordinary crawl.
/// </summary>
public sealed class GithubLinkCards(IServiceProvider services)
{
    public const string HttpClientName = nameof(GithubLinkCards);
    private const string ApiUrl = "https://api.github.com/repos/";
    private const string AvatarSizeQuery = "s=96";

    private IServiceProvider Services { get; } = services;
    private MediaSettings Settings { get; } = services.GetRequiredService<MediaSettings>();
    private HttpClient HttpClient => field ??= Services.HttpClientFactory().CreateClient(HttpClientName);
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<GithubLink?> Get(GithubLinkTarget target, CancellationToken cancellationToken)
    {
        try {
            return target.Kind switch {
                GithubLinkKind.PullRequest => await GetPullRequest(target, cancellationToken).ConfigureAwait(false),
                GithubLinkKind.Issue => await GetIssue(target, cancellationToken).ConfigureAwait(false),
                GithubLinkKind.Commit => await GetCommit(target, cancellationToken).ConfigureAwait(false),
                GithubLinkKind.Repository => await GetRepository(target, cancellationToken).ConfigureAwait(false),
                _ => null,
            };
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to get the GitHub card for {Target}", target);
            return null;
        }
    }

    // Private methods

    private async Task<GithubLink?> GetIssue(GithubLinkTarget target, CancellationToken cancellationToken)
    {
        var json = await GetJson($"{target.Repo}/issues/{target.Number}", cancellationToken).ConfigureAwait(false);
        if (json is null)
            return null;

        // GitHub serves a pull request under its issue number too; the link then gets the pull request card
        if (json["pull_request"] is not null)
            return await GetPullRequest(target with { Kind = GithubLinkKind.PullRequest }, cancellationToken)
                .ConfigureAwait(false);

        var card = new GithubLinkCard {
            Kind = GithubLinkKind.Issue,
            Repo = target.Repo,
            Number = target.Number,
            Author = GetString(json, "user", "login"),
            CreatedAt = GetMoment(json, "created_at"),
            IssueType = GetString(json, "type", "name"),
            Assignees = GetStrings(json["assignees"], "login"),
            Labels = GetStrings(json["labels"], "name"),
        };
        return new GithubLink(GetString(json, "title"), "", card, GetAvatarUrl(json["user"]));
    }

    private async Task<GithubLink?> GetPullRequest(GithubLinkTarget target, CancellationToken cancellationToken)
    {
        var json = await GetJson($"{target.Repo}/pulls/{target.Number}", cancellationToken).ConfigureAwait(false);
        if (json is null)
            return null;

        var card = new GithubLinkCard {
            Kind = GithubLinkKind.PullRequest,
            Repo = target.Repo,
            Number = target.Number,
            Author = GetString(json, "user", "login"),
            CreatedAt = GetMoment(json, "created_at"),
            BaseRef = GetString(json, "base", "ref"),
            HeadRef = GetString(json, "head", "ref"),
            Additions = GetInt(json, "additions"),
            Deletions = GetInt(json, "deletions"),
            ChangedFiles = GetInt(json, "changed_files"),
        };
        return new GithubLink(GetString(json, "title"), "", card, GetAvatarUrl(json["user"]));
    }

    private async Task<GithubLink?> GetCommit(GithubLinkTarget target, CancellationToken cancellationToken)
    {
        var json = await GetJson($"{target.Repo}/commits/{target.Sha}", cancellationToken).ConfigureAwait(false);
        if (json is null)
            return null;

        // The committer may have no GitHub account, and then the name on the commit is all there is
        var author = GetString(json, "author", "login").NullIfEmpty() ?? GetString(json, "commit", "author", "name");
        var card = new GithubLinkCard {
            Kind = GithubLinkKind.Commit,
            Repo = target.Repo,
            Sha = GetString(json, "sha").NullIfEmpty() ?? target.Sha,
            Author = author,
            CreatedAt = GetMoment(json, "commit", "author", "date"),
            Additions = GetInt(json, "stats", "additions"),
            Deletions = GetInt(json, "stats", "deletions"),
            ChangedFiles = (json["files"] as JsonArray)?.Count ?? 0,
        };
        var message = GetString(json, "commit", "message");
        var title = message.Split('\n', 2)[0].Trim();
        return new GithubLink(title, "", card, GetAvatarUrl(json["author"]));
    }

    private async Task<GithubLink?> GetRepository(GithubLinkTarget target, CancellationToken cancellationToken)
    {
        var json = await GetJson(target.Repo, cancellationToken).ConfigureAwait(false);
        if (json is null)
            return null;

        var card = new GithubLinkCard {
            Kind = GithubLinkKind.Repository,
            Repo = GetString(json, "full_name").NullIfEmpty() ?? target.Repo,
            Author = GetString(json, "owner", "login"),
            CreatedAt = GetMoment(json, "created_at"),
            Language = GetString(json, "language"),
            DefaultBranch = GetString(json, "default_branch"),
        };
        return new GithubLink(card.Repo, GetString(json, "description"), card, GetAvatarUrl(json["owner"]));
    }

    private async Task<JsonNode?> GetJson(string path, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource(Settings.CrawlTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl + path);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!Settings.GithubApiKey.IsNullOrEmpty())
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.GithubApiKey);

        using var response = await HttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode) {
            var remaining = response.Headers.TryGetValues("x-ratelimit-remaining", out var values)
                ? values.FirstOrDefault()
                : null;
            Log.LogWarning("GitHub API answered {StatusCode} for {Path}, rate limit remaining: {Remaining}",
                (int)response.StatusCode, path, remaining ?? "?");
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        return await JsonNode.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);
    }

    private static string GetAvatarUrl(JsonNode? user)
    {
        var url = GetString(user, "avatar_url");
        if (url.IsNullOrEmpty())
            return "";

        return url + (url.Contains('?') ? "&" : "?") + AvatarSizeQuery;
    }

    private static int GetInt(JsonNode? json, params ReadOnlySpan<string> path)
    {
        foreach (var key in path)
            json = json?[key];
        return json is JsonValue value && value.TryGetValue<int>(out var i) ? i : 0;
    }

    private static Moment GetMoment(JsonNode? json, params ReadOnlySpan<string> path)
        => DateTime.TryParse(GetString(json, path), null, DateTimeStyles.AdjustToUniversal, out var dateTime)
            ? new Moment(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc))
            : default;

    private static string[] GetStrings(JsonNode? array, string key)
        => array is JsonArray items
            ? items.Select(x => GetString(x, key)).Where(x => !x.IsNullOrEmpty()).ToArray()
            : [];

    private static string GetString(JsonNode? json, params ReadOnlySpan<string> path)
    {
        foreach (var key in path)
            json = json?[key];
        return json is JsonValue value && value.TryGetValue<string>(out var s) ? s : "";
    }
}

/// <summary>
/// The preview of a GitHub link: the page title, a description where the item has one (a repository),
/// the card, and the avatar of the author - of the owner for a repository - sized for the card.
/// </summary>
public sealed record GithubLink(string Title, string Description, GithubLinkCard Card, string AvatarUrl);
