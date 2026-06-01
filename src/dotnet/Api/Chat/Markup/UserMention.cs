using ActualChat.Users;

namespace ActualChat.Chat;

public sealed class UserMention(MentionRef id, string name = "") : MentionMarkup(id, name)
{
    public UserId UserId => (UserId)Id.Target;
    public Account? Account { get; init; }
    public bool IsChatMember { get; init; }
}
