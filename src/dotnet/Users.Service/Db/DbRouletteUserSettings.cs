using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Roulette;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Users.Db;

[Table("RouletteUserSettings")]
public class DbRouletteUserSettings : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!; // The same as user account id.
    [ConcurrencyCheck] public long Version { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DbRouletteUserSettings() { }
    public DbRouletteUserSettings(RouletteUserSettings model) => UpdateFrom(model);

    public RouletteUserSettings ToModel()
        => new (new UserId(Id), Version) {
            IsEnabled = IsEnabled
        };

    public void UpdateFrom(RouletteUserSettings model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;

        IsEnabled = model.IsEnabled;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbRouletteUserSettings>
    {
        public void Configure(EntityTypeBuilder<DbRouletteUserSettings> builder)
            => builder.Property(a => a.Id).IsRequired();
    }
}
