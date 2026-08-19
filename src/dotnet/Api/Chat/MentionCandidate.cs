using ActualChat.Search;

namespace ActualChat.Chat;

public sealed record MentionCandidate(
    MentionRef Id,
    string Title,
    Picture? Picture,
    SearchDocument SearchDocument)
{
    public bool IsChatMember { get; init; }
    // The name to bake into the inserted mention; null = Title, which may add the account name in brackets
    public string? MentionName { get; init; }
    public PlaceId? PlaceId { get; init; }
    public string? PlaceTitle { get; init; }
}
