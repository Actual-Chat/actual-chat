namespace ActualChat.Chat;

public sealed record MentionCandidate(
    MentionId Id,
    MentionCandidateKind Kind,
    string Title,
    Picture? Picture,
    string NormalizedSearchText)
{
    public bool IsChatMember { get; init; }
    public PlaceId? PlaceId { get; init; }
    public string? PlaceTitle { get; init; }
}
