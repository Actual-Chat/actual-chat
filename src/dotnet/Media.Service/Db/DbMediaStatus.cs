using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Media.Db;

[Table("MediaStatuses")]
public class DbMediaStatus : IHasId<string>, IRequirementTarget
{
    public DbMediaStatus() { }
    public DbMediaStatus(MediaStatusInfo model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = "";

    public MediaStatus Status { get; set; }
    public MediaPreparingStage PreparingStage { get; set; }
    public double StageProgress { get; set; }

    public MediaStatusInfo ToModel()
        => new (MediaId.Parse(Id), Status, PreparingStage, StageProgress);

    public void UpdateFrom(MediaStatusInfo model)
    {
        Id = model.Id.Value;
        Status = model.Status;
        PreparingStage = model.PreparingStage;
        StageProgress = model.StageProgress;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbMediaStatus>
    {
        public void Configure(EntityTypeBuilder<DbMediaStatus> builder)
            => builder.Property(x => x.Id).UseCollation("C");
    }
}
