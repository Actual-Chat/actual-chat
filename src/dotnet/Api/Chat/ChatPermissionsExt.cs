
namespace ActualChat.Chat;

public static class ChatPermissionsExt
{
    // The permission set of the Moderator system role; Role.Fix and DbRole.ToModel clamp to it,
    // so changing it here retroactively applies to every existing Moderator role.
    public static readonly ChatPermissions Moderator =
        ChatPermissions.Moderate
        | ChatPermissions.Write
        | ChatPermissions.SeeMembers
        | ChatPermissions.Leave;

    public static ChatPermissions AddImplied(this ChatPermissions permissions)
    {
        if (permissions.Has(ChatPermissions.Owner))
            permissions |=
                ChatPermissions.Moderate
                | ChatPermissions.EditRoles
                | ChatPermissions.EditProperties
                | ChatPermissions.EditMembers
                | ChatPermissions.SeeMembers
                | ChatPermissions.Invite
                | ChatPermissions.Write;
        if (permissions.Has(ChatPermissions.Moderate))
            permissions |=
                ChatPermissions.EditProperties
                | ChatPermissions.EditMembers;
        if (permissions.Has(ChatPermissions.EditMembers))
            permissions |= ChatPermissions.SeeMembers;
        if (permissions.Has(ChatPermissions.Invite))
            permissions |=
                ChatPermissions.Join
                | ChatPermissions.SeeMembers;
        if (permissions.Has(ChatPermissions.Write))
            permissions |=
                ChatPermissions.Upload
                | ChatPermissions.WriteAudio
                | ChatPermissions.WriteVideo;
        if (permissions.Has(ChatPermissions.Join) || permissions.Has(ChatPermissions.Write))
            permissions |= ChatPermissions.Read;
        if (permissions.Has(ChatPermissions.Read))
            permissions |=
                ChatPermissions.ReadAudio
                | ChatPermissions.ReadVideo;
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
