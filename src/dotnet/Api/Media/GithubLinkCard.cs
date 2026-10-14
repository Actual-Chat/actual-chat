namespace ActualChat.Media;

public enum GithubLinkKind
{
    None = 0,
    PullRequest,
    Issue,
    Commit,
    Repository,
}

/// <summary>
/// What a GitHub link preview shows besides the page title: the kind of item the link points to,
/// its repository, and the fields that are settled once the item is filed. The state, the checks
/// and the review decision are left out on purpose: they change too fast for a daily refresh.
/// </summary>
public sealed record GithubLinkCard
{
    private const char ListSeparator = '\n';

    public GithubLinkKind Kind { get; init; }
    public string Repo { get; init; } = "";
    public int Number { get; init; }
    public string Sha { get; init; } = "";
    public string Author { get; init; } = "";
    public Moment CreatedAt { get; init; }
    public string BaseRef { get; init; } = "";
    public string HeadRef { get; init; } = "";
    public int Additions { get; init; }
    public int Deletions { get; init; }
    public int ChangedFiles { get; init; }
    public string IssueType { get; init; } = "";
    public string[] Assignees { get; init; } = [];
    public string[] Labels { get; init; } = [];
    public string Language { get; init; } = "";
    public string DefaultBranch { get; init; } = "";

    public string ShortSha => Sha.Length > 7 ? Sha[..7] : Sha;

    // Protected/internal methods

    internal static GithubLinkCard? FromMetadata(IHasMetadata source)
    {
        if (!Enum.TryParse<GithubLinkKind>(source.GetMetadataValue("", "GithubKind"), out var kind)
            || kind == GithubLinkKind.None)
            return null;

        return new GithubLinkCard {
            Kind = kind,
            Repo = source.GetMetadataValue("", "GithubRepo"),
            Number = source.GetMetadataValue(0, "GithubNumber"),
            Sha = source.GetMetadataValue("", "GithubSha"),
            Author = source.GetMetadataValue("", "GithubAuthor"),
            CreatedAt = source.GetMetadataValue(default(Moment), "GithubCreatedAt"),
            BaseRef = source.GetMetadataValue("", "GithubBaseRef"),
            HeadRef = source.GetMetadataValue("", "GithubHeadRef"),
            Additions = source.GetMetadataValue(0, "GithubAdditions"),
            Deletions = source.GetMetadataValue(0, "GithubDeletions"),
            ChangedFiles = source.GetMetadataValue(0, "GithubChangedFiles"),
            IssueType = source.GetMetadataValue("", "GithubIssueType"),
            Assignees = SplitList(source.GetMetadataValue("", "GithubAssignees")),
            Labels = SplitList(source.GetMetadataValue("", "GithubLabels")),
            Language = source.GetMetadataValue("", "GithubLanguage"),
            DefaultBranch = source.GetMetadataValue("", "GithubDefaultBranch"),
        };
    }

    internal static void WriteMetadata(IHasMetadata target, GithubLinkCard? card)
    {
        // A null clears every key, so a preview re-crawled as an ordinary page loses the card
        target.SetMetadataValue(card?.Kind.ToString(), "GithubKind");
        target.SetMetadataValue(card?.Repo, "GithubRepo");
        target.SetMetadataValue(card?.Number, "GithubNumber");
        target.SetMetadataValue(card?.Sha.NullIfEmpty(), "GithubSha");
        target.SetMetadataValue(card?.Author, "GithubAuthor");
        target.SetMetadataValue(card?.CreatedAt, "GithubCreatedAt");
        target.SetMetadataValue(card?.BaseRef.NullIfEmpty(), "GithubBaseRef");
        target.SetMetadataValue(card?.HeadRef.NullIfEmpty(), "GithubHeadRef");
        target.SetMetadataValue(card?.Additions, "GithubAdditions");
        target.SetMetadataValue(card?.Deletions, "GithubDeletions");
        target.SetMetadataValue(card?.ChangedFiles, "GithubChangedFiles");
        target.SetMetadataValue(card?.IssueType.NullIfEmpty(), "GithubIssueType");
        target.SetMetadataValue(JoinList(card?.Assignees), "GithubAssignees");
        target.SetMetadataValue(JoinList(card?.Labels), "GithubLabels");
        target.SetMetadataValue(card?.Language.NullIfEmpty(), "GithubLanguage");
        target.SetMetadataValue(card?.DefaultBranch.NullIfEmpty(), "GithubDefaultBranch");
    }

    // Private methods

    private static string[] SplitList(string value)
        => value.Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries);

    private static string? JoinList(string[]? items)
        => items is { Length: > 0 } ? string.Join(ListSeparator, items) : null;
}
