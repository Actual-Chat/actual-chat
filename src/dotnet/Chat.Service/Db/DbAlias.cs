using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("UserLinks")] // TODO(AY): Rename to Aliases
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbAlias : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public AliasKind Kind { get; set; }
    public string TargetId { get; set; } = "";

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbAlias() { }

    public DbAlias(Alias alias)
    {
        var id = alias.Id.NormalizedValue;
        this.RequireSameOrEmptyId(id);
        alias.RequireSomeVersion();

        Id = id;
        Version = alias.Version;
        CreatedAt = alias.CreatedAt.ToDateTimeClamped();
        Kind = alias.Kind;
        TargetId = alias.TargetId;
    }

    public Alias ToModel()
        => new (AliasId.Parse(Id), Version) {
            CreatedAt = CreatedAt,
            Kind = Kind,
            TargetId = TargetId,
        };
}
