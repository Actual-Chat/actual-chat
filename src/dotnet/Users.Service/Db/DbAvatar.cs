using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Users.Db;

/// <summary>
/// User avatar. <br />
/// </summary>
[Table("Avatars")]
public class DbAvatar : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    string IHasId<string>.Id => Id;
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string ChatPrincipalId { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Picture { get; set; } = "";
    public string Bio { get; set; } = "";

    public DbAvatar() { }
    public DbAvatar(AvatarFull model) => UpdateFrom(model);

    public AvatarFull ToModel()
        => new() {
            Id = Id,
            Version = Version,
            ChatPrincipalId = ChatPrincipalId,
            Name = Name,
            Picture = Picture,
            Bio = Bio,
        };

    public void UpdateFrom(AvatarFull model)
    {
        if (Id.IsNullOrEmpty())
            Id = model.Id;
        else if (model.Id != Id)
            throw StandardError.Constraint("Can't change Avatar Id.");

        if (ChatPrincipalId.IsNullOrEmpty())
            ChatPrincipalId = model.ChatPrincipalId;
        else if (ChatPrincipalId != model.ChatPrincipalId)
            throw StandardError.Constraint("Can't change Avatar ChatPrincipalId.");

        Version = model.Version;
        Name = model.Name;
        Picture = model.Picture;
        Bio = model.Bio;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbAvatar>
    {
        public void Configure(EntityTypeBuilder<DbAvatar> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.ChatPrincipalId).IsRequired();
            builder.Property(a => a.Name).IsRequired();
        }
    }
}
