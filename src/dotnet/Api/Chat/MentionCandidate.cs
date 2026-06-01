using ActualChat.Search;

namespace ActualChat.Chat;

public sealed record MentionCandidate(
    MentionRef Id,
    string Title,
    Picture? Picture,
    SearchDocument SearchDocument)
{
    public bool IsChatMember { get; init; }
    public PlaceId? PlaceId { get; init; }
    public string? PlaceTitle { get; init; }
}
