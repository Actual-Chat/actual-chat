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

    public MediaStage Stage { get; set; }
    public double StageProgress { get; set; }
    public string ErrorMessage { get; set; } = "";

    public MediaProgress ToModel()
        => new (MediaId.Parse(Id), Version, Stage, StageProgress, ErrorMessage);

    public void UpdateFrom(MediaProgress model)
    {
        Id = model.Id.Value;
        Version = model.Version;
        Stage = model.Stage;
        StageProgress = model.StageProgress;
        ErrorMessage = model.ErrorMessage;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbMediaProgress>
    {
        public void Configure(EntityTypeBuilder<DbMediaProgress> builder)
            => builder.Property(x => x.Id).UseCollation("C");
    }
}
