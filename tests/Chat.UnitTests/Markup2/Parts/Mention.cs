namespace ActualChat.Chat.UnitTests.Markup2;

public enum MentionKind { Unknown, UserId, AuthorId }

public sealed record Mention(
    string Target,
    MentionKind Kind = MentionKind.Unknown
    ) : TextMarkup
{
    public Mention() : this("") { }

    public override string ToPlainText()
        => Kind switch {
            MentionKind.UserId => $"@u:{Target}",
            MentionKind.AuthorId => $"@a:{Target}",
            _ => $"@{Target}",
        };
}
