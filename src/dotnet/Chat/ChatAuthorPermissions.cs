namespace ActualChat.Chat;

[DataContract]
public record ChatAuthorPermissions(
    [property: DataMember] ChatAuthor? Author,
    [property: DataMember] User? User,
    [property: DataMember] ChatPermissions Permissions)
{
    public bool CanRead => Permissions.Has(ChatPermissions.Read);
    public bool CanWrite => Permissions.Has(ChatPermissions.Write);
    public bool CanReadWrite => Permissions.Has(ChatPermissions.ReadWrite);
    public bool CanInvite => Permissions.Has(ChatPermissions.Invite);
    public bool CanEditProperties => Permissions.Has(ChatPermissions.EditProperties);
    public bool IsOwner => Permissions.Has(ChatPermissions.Owner);

    public static ChatAuthorPermissions None { get; } = new(null, null, ChatPermissions.None);

    public bool Has(ChatPermissions required)
        => Permissions.Has(required);
    public void Demand(ChatPermissions required)
        => Permissions.Demand(required);
}

