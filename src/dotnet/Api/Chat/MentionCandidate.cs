using ActualChat.Search;

namespace ActualChat.Chat;

public sealed record MentionCandidate(
    MentionId Id,
    string Title,
    Picture? Picture,
    MemSearchDocument MemSearchDocument)
{
    public bool IsChatMember { get; init; }
    public PlaceId? PlaceId { get; init; }
    public string? PlaceTitle { get; init; }
}
