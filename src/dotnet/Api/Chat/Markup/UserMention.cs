using ActualChat.Users;

namespace ActualChat.Chat;

public sealed class UserMention(MentionId id, string name = "") : MentionMarkup(id, name)
{
    public UserId UserId => (UserId)Id.TargetId;
    public Account? Account { get; init; }
    public bool IsChatMember { get; init; }
}
