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

    public MediaStage Stage { get; set; }
    public double StageProgress { get; set; }
    public string ErrorMessage { get; set; } = "";

    public MediaStatusInfo ToModel()
        => new (MediaId.Parse(Id), Stage, StageProgress, ErrorMessage);

    public void UpdateFrom(MediaStatusInfo model)
    {
        Id = model.Id.Value;
        Stage = model.Stage;
        StageProgress = model.StageProgress;
        ErrorMessage = model.ErrorMessage;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbMediaStatus>
    {
        public void Configure(EntityTypeBuilder<DbMediaStatus> builder)
            => builder.Property(x => x.Id).UseCollation("C");
    }
}
