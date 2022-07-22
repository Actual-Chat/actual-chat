using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract]
public record ChatAuthorRules(
    [property: DataMember] Symbol ChatId,
    [property: DataMember] ChatAuthor? Author,
    [property: DataMember] Account? Account,
    [property: DataMember] ChatPermissions Permissions = 0)
{
    public static ChatAuthorRules None(Symbol chatId) => new(chatId, null, null);

    public bool CanRead() => Permissions.Has(ChatPermissions.Read);
    public bool CanWrite() => Permissions.Has(ChatPermissions.Write);
    public bool CanJoin() => Permissions.Has(ChatPermissions.Join);
    public bool CanInvite() => Permissions.Has(ChatPermissions.Invite);
    public bool CanEditProperties() => Permissions.Has(ChatPermissions.EditProperties);
    public bool CanEditRoles() => Permissions.Has(ChatPermissions.EditRoles);
    public bool IsOwner() => Permissions.Has(ChatPermissions.Owner);

    public bool Has(ChatPermissions required)
        => Permissions.Has(required);
    public void Require(ChatPermissions required)
        => Permissions.Require(required);
}
