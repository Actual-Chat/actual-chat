namespace ActualChat.Chat;

/// <summary>
/// One mentionable entry surfaced by <c>MentionIndexUI</c>. The <see cref="Words"/> array
/// is pre-tokenized lowercase for fast prefix matching (see <c>MentionFilter</c>).
/// </summary>
public sealed record MentionCandidate(
    MentionId Id,
    MentionCandidateKind Kind,
    string PrimaryName,
    string? SecondaryName,
    Picture? Picture,
    string[] Words)
{
    public bool IsChatMember { get; init; }
    // The place the candidate belongs to. Null for users, emojis, and standalone
    // group chats. Set for place-chats (the chat's own place) and for place mentions
    // (the place itself). Used by the picker / view layer to decide whether to show
    // a "| PlaceTitle" suffix on the displayed name.
    public PlaceId? PlaceId { get; init; }
    public string? PlaceTitle { get; init; }
}
