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
}
