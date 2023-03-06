using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Medias.Db;

[Table("Medias")]
public class DbMedia : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt = CoarseSystemClock.Now;

    public DbMedia() { }
    public DbMedia(Media model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }

    public string FileName { get; set; } = "";
    public string ContentId { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Length { get; set; }
    public bool IsRemoved { get; set; }

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public Media ToModel()
        => new (new MediaId(Id), Version) {
            FileName = FileName,
            ContentId = ContentId,
            ContentType = ContentType,
            Length = Length,
            IsRemoved = IsRemoved,
        };

    public void UpdateFrom(Media model)
    {
        this.RequireSameOrEmptyId(model.Id);
        model.RequireSomeVersion();

        Id = model.Id;
        Version = model.Version;
        ContentId = model.ContentId;
        FileName = model.FileName;
        ContentType = model.ContentType;
        Length = model.Length;
        IsRemoved = model.IsRemoved;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbMedia>
    {
        public void Configure(EntityTypeBuilder<DbMedia> builder) {
            builder.Property(a => a.FileName).IsRequired();
            builder.Property(a => a.ContentType).IsRequired();
            builder.Property(a => a.ContentId).IsRequired();
        }
    }
}
