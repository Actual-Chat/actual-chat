using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Generators;
using Stl.Versioning;

namespace ActualChat.Users.Db;

[Table("Avatars")]
public class DbAvatar : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    public static RandomStringGenerator IdGenerator { get; } = new(10, Alphabet.AlphaNumeric);

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string UserId { get; set; }
    public string Name { get; set; } = "";
    public string Picture { get; set; } = "";
    public string Bio { get; set; } = "";

    public DbAvatar() { }
    public DbAvatar(AvatarFull model) => UpdateFrom(model);

    public AvatarFull ToModel()
        => new(Id, Version) {
            UserId = new UserId(UserId),
            Name = Name,
            Picture = Picture,
            Bio = Bio,
        };

    public void UpdateFrom(AvatarFull model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        if (UserId.IsNullOrEmpty())
            UserId = model.UserId;
        else if (model.UserId != (Symbol)UserId)
            throw StandardError.Constraint("Can't change Avatar.UserId.");

        Id = id;
        Version = model.Version;
        Name = model.Name;
        Picture = model.Picture;
        Bio = model.Bio;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbAvatar>
    {
        public void Configure(EntityTypeBuilder<DbAvatar> builder)
            => builder.Property(a => a.Id).IsRequired();
    }
}
