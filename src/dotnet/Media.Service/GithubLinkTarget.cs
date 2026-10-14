namespace ActualChat.Media;

/// <summary>
/// The item a GitHub link points to: a pull request or issue by number, a commit by SHA, or the
/// repository itself.
/// </summary>
public sealed record GithubLinkTarget(
    GithubLinkKind Kind,
    string Owner,
    string Name,
    int Number = 0,
    string Sha = "")
{
    public string Repo => $"{Owner}/{Name}";
}
