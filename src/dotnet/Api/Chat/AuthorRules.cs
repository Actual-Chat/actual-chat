using ActualChat.Users;

namespace ActualChat.Chat;

/// <summary>
/// Encapsulates an author's resolved permissions for a chat.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record AuthorRules(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] AuthorFull? Author,
    [property: DataMember, Key(2)] AccountFull? Account,
    [property: DataMember, Key(3)] ChatPermissions Permissions = default
    ) : IRequirementTarget
{
    public static readonly Requirement<AuthorRules> MustExist = Requirement.New(
        (AuthorRules? a) => a?.ChatId is not null,
        new(() => StandardError.NotFound<AuthorRules>()));

    public static AuthorRules None(ChatId chatId) => new(chatId, null, null);

    public bool CanRead() => Permissions.Has(ChatPermissions.Read);
    public bool CanWrite() => Permissions.Has(ChatPermissions.Write);
    public bool CanUpload() => Permissions.Has(ChatPermissions.Upload);
    public bool CanWriteAudio() => Permissions.Has(ChatPermissions.WriteAudio);
    public bool CanWriteVideo() => Permissions.Has(ChatPermissions.WriteVideo);
    public bool CanReadAudio() => Permissions.Has(ChatPermissions.ReadAudio);
    public bool CanReadVideo() => Permissions.Has(ChatPermissions.ReadVideo);
    public bool CanSeeMembers() => Permissions.Has(ChatPermissions.SeeMembers);
    public bool CanJoin() => Permissions.Has(ChatPermissions.Join);
    public bool CanLeave() => Permissions.Has(ChatPermissions.Leave);
    public bool CanInvite() => Permissions.Has(ChatPermissions.Invite);
    public bool CanEditProperties() => Permissions.Has(ChatPermissions.EditProperties);
    public bool CanEditRoles() => Permissions.Has(ChatPermissions.EditRoles);
    public bool CanEditMembers() => Permissions.Has(ChatPermissions.EditMembers);
    public bool IsOwner() => Permissions.Has(ChatPermissions.Owner);

    public bool Has(ChatPermissions required)
        => Permissions.Has(required);
    public void Require(ChatPermissions required)
        => Permissions.Require(required);

    // This record relies on referential equality
    public bool Equals(AuthorRules? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
