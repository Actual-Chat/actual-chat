using System.Security;

namespace ActualChat.Chat;

public static class ChatPermissionsExt
{
    public static ChatPermissions AddImplied(this ChatPermissions permissions)
    {
        if (permissions.Has(ChatPermissions.Owner))
            permissions |=
                ChatPermissions.EditRoles
                | ChatPermissions.EditProperties
                | ChatPermissions.EditMembers
                | ChatPermissions.SeeMembers
                | ChatPermissions.Invite
                | ChatPermissions.Write;
        if (permissions.Has(ChatPermissions.EditMembers))
            permissions |= ChatPermissions.SeeMembers;
        if (permissions.Has(ChatPermissions.Invite))
            permissions |=
                ChatPermissions.Join
                | ChatPermissions.SeeMembers;
        if (permissions.Has(ChatPermissions.Join) || permissions.Has(ChatPermissions.Write))
            permissions |= ChatPermissions.Read;
        return permissions;
    }

    public static bool Has(this ChatPermissions available, ChatPermissions required)
        => (available & required) == required;

    public static void Require(this ChatPermissions available, ChatPermissions required)
    {
        if (!Has(available, required))
            throw NotEnoughPermissions(required);
    }

    public static Exception NotEnoughPermissions(ChatPermissions? required = null)
        => StandardError.NotEnoughPermissions(required?.ToString());
}
