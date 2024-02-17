namespace ActualChat.Chat;

public static class RoleExt
{
    public static ChatPermissions ToPermissions(this IEnumerable<Role> roles)
        => roles.Aggregate((ChatPermissions) 0, (acc, role) => acc | role.Permissions);
}
