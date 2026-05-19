namespace ActualChat.Chat;

public enum MentionCandidateKind
{
    User = 0,
    Chat = 1,
    Emoji = 2,
}

[Flags]
public enum MentionKindFilter
{
    None = 0,
    User = 1 << MentionCandidateKind.User,
    Chat = 1 << MentionCandidateKind.Chat,
    Emoji = 1 << MentionCandidateKind.Emoji,
    All = User | Chat | Emoji,
}

public static class MentionKindFilterExt
{
    public static bool Allows(this MentionKindFilter filter, MentionCandidateKind kind)
        => (filter & (MentionKindFilter)(1 << (int)kind)) != 0;
}
