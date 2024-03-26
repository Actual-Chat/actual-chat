using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Media.Db;

[Table("Media")]
public class DbMedia : IHasId<string>, IRequirementTarget
{
    public DbMedia() { }
    public DbMedia(Media model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = "";

    public string Scope { get; set; } = "";
    public string LocalId { get; set; } = "";
    public string ContentId { get; set; } = "";
    public string MetadataJson { get; set; } = "";

    public Media ToModel()
        => new (new MediaId(Id, Scope, LocalId, AssumeValid.Option)) {
            ContentId = ContentId,
            MetadataJson = MetadataJson,
        };

    public void UpdateFrom(Media model)
    {
        this.RequireSameOrEmptyId(model.Id);

        if (!Id.IsNullOrEmpty())
            return;

        Id = model.Id;
        Scope = model.Id.Scope;
        LocalId = model.Id.LocalId;
        ContentId = model.ContentId;
        MetadataJson = model.MetadataJson;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbMedia>
    {
        public void Configure(EntityTypeBuilder<DbMedia> builder)
            => builder.HasIndex(a => a.ContentId);
    }
}
