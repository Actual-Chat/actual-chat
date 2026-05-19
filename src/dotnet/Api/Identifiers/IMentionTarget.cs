namespace ActualChat;

/// <summary>
/// Marker for identifier types that can appear as the target of a <see cref="MentionId"/>.
/// Each implementation is paired with one <see cref="MentionKind"/> that defines its prefix.
/// </summary>
public interface IMentionTarget : IStringLike
{
    MentionKind MentionKind { get; }
}
