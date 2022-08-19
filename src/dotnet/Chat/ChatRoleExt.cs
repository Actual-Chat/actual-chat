namespace ActualChat.Chat;

public static class ChatRoleExt
{
    public static ChatPermissions ToPermissions(this IEnumerable<ChatRole> roles)
        => roles.Aggregate((ChatPermissions) 0, (acc, role) => acc | role.Permissions);
}
