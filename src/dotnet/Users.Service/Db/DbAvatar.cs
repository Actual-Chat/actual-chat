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

    public string? PrincipalId { get; set; }
    public string Name { get; set; } = "";
    public string Picture { get; set; } = "";
    public string Bio { get; set; } = "";

    public DbAvatar() { }
    public DbAvatar(AvatarFull model) => UpdateFrom(model);

    public AvatarFull ToModel()
        => new() {
            Id = Id,
            Version = Version,
            PrincipalId = PrincipalId ?? Symbol.Empty,
            Name = Name,
            Picture = Picture,
            Bio = Bio,
        };

    public void UpdateFrom(AvatarFull model)
    {
        if (Id.IsNullOrEmpty())
            Id = model.Id;
        else if (model.Id != Id)
            throw StandardError.Constraint("Can't change Avatar.Id.");

        if (PrincipalId.IsNullOrEmpty())
            PrincipalId = model.PrincipalId.NullIfEmpty()?.Value;
        else if (PrincipalId != model.PrincipalId)
            throw StandardError.Constraint("Can't change Avatar.PrincipalId.");

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
