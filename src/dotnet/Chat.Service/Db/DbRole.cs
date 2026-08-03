using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("Roles")]
[Index(nameof(ChatId), nameof(LocalId), IsUnique = true)]
[Index(nameof(ChatId), nameof(Name))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbRole : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = null!;
    public long LocalId { get; set; }

    [Column(TypeName = "smallint")]
    public SystemRole SystemRole { get; set; }
    public string Name { get; set; } = "";
    public string Picture { get; set; } = "";
    public bool CanRead { get; set; }
    public bool CanWrite { get; set; }
    public bool CanJoin { get; set; }
    public bool CanLeave { get; set; }
    public bool CanInvite { get; set; }
    public bool CanEditProperties { get; set; }
    public bool CanEditRoles { get; set; }
    public bool CanSeeMembers { get; set; }
    public bool CanModerate { get; set; }

    public DbRole() { }
    public DbRole(Role model) => UpdateFrom(model);

    public Role ToModel()
    {
        var permissions = (ChatPermissions) 0;
        if (CanRead)
            permissions |= ChatPermissions.Read;
        if (CanWrite)
            permissions |= ChatPermissions.Write;
        if (CanSeeMembers)
            permissions |= ChatPermissions.SeeMembers;
        if (CanJoin)
            permissions |= ChatPermissions.Join;
        if (CanLeave)
            permissions |= ChatPermissions.Leave;
        if (CanInvite)
            permissions |= ChatPermissions.Invite;
        if (CanEditProperties)
            permissions |= ChatPermissions.EditProperties;
        if (CanEditRoles)
            permissions |= ChatPermissions.EditRoles;
        if (CanModerate)
            permissions |= ChatPermissions.Moderate;

        // Fix system role permissions
        if (SystemRole is SystemRole.Owner)
            permissions = ChatPermissions.Owner;
        else if (SystemRole is SystemRole.Moderator)
            permissions = ChatPermissionsExt.Moderator;

        return new (RoleId.Parse(Id), Version) {
            SystemRole = SystemRole,
            Name = Name,
            Picture = Picture,
            Permissions = permissions.AddImplied(),
        };
    }

    public void UpdateFrom(Role model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireVersion();

        Id = id.Value;
        ChatId = id.ChatId.Value;
        LocalId = id.LocalId;
        Version = model.Version;
        SystemRole = model.SystemRole;
        Name = model.Name;
        Picture = model.Picture;
        CanRead = model.Permissions.Has(ChatPermissions.Read);
        CanWrite = model.Permissions.Has(ChatPermissions.Write);
        CanSeeMembers = model.Permissions.Has(ChatPermissions.SeeMembers);
        CanJoin = model.Permissions.Has(ChatPermissions.Join);
        CanLeave = model.Permissions.Has(ChatPermissions.Leave);
        CanInvite = model.Permissions.Has(ChatPermissions.Invite);
        CanEditProperties = model.Permissions.Has(ChatPermissions.EditProperties);
        CanEditRoles = model.Permissions.Has(ChatPermissions.EditRoles);
        CanModerate = model.Permissions.Has(ChatPermissions.Moderate);
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbRole>
    {
        public void Configure(EntityTypeBuilder<DbRole> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.ChatId).IsRequired();
            builder.Property(a => a.Name).IsRequired();
        }
    }
}
