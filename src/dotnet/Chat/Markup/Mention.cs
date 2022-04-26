namespace ActualChat.Chat;

public enum MentionKind { Unknown, UserId, AuthorId }

public sealed record Mention(
    string Target,
    MentionKind Kind = MentionKind.Unknown
    ) : TextMarkup
{
    public Mention() : this("") { }

    public override string ToMarkupText()
        => Kind switch {
            MentionKind.UserId => $"@u:{Target}",
            MentionKind.AuthorId => $"@a:{Target}",
            _ => $"@{Target}",
        };
}
