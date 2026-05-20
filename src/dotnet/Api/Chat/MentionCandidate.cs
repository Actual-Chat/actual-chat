namespace ActualChat.Chat;

/// <summary>
/// One mentionable entry surfaced by <c>MentionIndexUI</c>. <see cref="SearchText"/> is a
/// pre-normalized lowercase blob (each token preceded by a space, place name first for
/// place chats) used for fast prefix matching — see <c>MentionFilter</c>.
/// </summary>
public sealed record MentionCandidate(
    MentionId Id,
    MentionCandidateKind Kind,
    string Title,
    Picture? Picture,
    string SearchText)
{
    public bool IsChatMember { get; init; }
    // The place the candidate belongs to. Null for users, emojis, and standalone
    // group chats. Set for place-chats (the chat's own place) and for place mentions
    // (the place itself). Used by the picker / view layer to render the place context.
    public PlaceId? PlaceId { get; init; }
    public string? PlaceTitle { get; init; }
}
