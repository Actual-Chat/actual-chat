using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Media.Db;

[Table("Media")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbMedia : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    public DbMedia() { }
    public DbMedia(MediaFull model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }

    public string Scope { get; set; } = "";
    public string LocalId { get; set; } = "";
    public string ContentId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string MetadataJson { get; set; } = "";

    public MediaFull ToModel()
        => new (MediaId.Parse(Id)) {
            Version = Version,
            ContentId = ContentId,
            UserId = ActualChat.UserId.ParseNullable(UserId),
            Metadata = MetadataSerializer.Read(MetadataJson),
        };

    public void UpdateFrom(MediaFull model)
    {
        var isNew = Id.IsNullOrEmpty();
        this.RequireSameOrEmptyId(model.Id.Value);

        if (isNew) {
            Id = model.Id.Value;
            Scope = model.Id.Scope;
            LocalId = model.Id.LocalId;
            UserId = model.UserId?.Value ?? "";
        }
        Version = model.Version;
        ContentId = model.ContentId;
        MetadataJson = MetadataSerializer.Write(model.Metadata);
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbMedia>
    {
        public void Configure(EntityTypeBuilder<DbMedia> builder)
            => builder.HasIndex(a => a.ContentId);
    }
}
