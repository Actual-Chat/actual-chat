using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Media.Db;

[Table("MediaProgresses")]
public class DbMediaProgress : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    public DbMediaProgress() { }
    public DbMediaProgress(MediaProgress model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }

    public MediaProcessingStage Stage { get; set; }
    public double StageProgress { get; set; }
    public string Error { get; set; } = "";

    public MediaProgress ToModel()
        => new (MediaId.Parse(Id), Version, Stage, StageProgress, Error.NullIfEmpty());

    public void UpdateFrom(MediaProgress model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireSomeVersion();

        Id = id.Value;
        Version = model.Version;
        Stage = model.Stage;
        StageProgress = model.StageProgress;
        Error = model.Error ?? "";
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbMediaProgress>
    {
        public void Configure(EntityTypeBuilder<DbMediaProgress> builder)
            => builder.Property(x => x.Id).UseCollation("C");
    }
}
