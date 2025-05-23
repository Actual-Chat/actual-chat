using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Media.Db;

[Table("GrabStatuses")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbGrabStatus : IHasId<string>, IRequirementTarget
{
    public DbGrabStatus() { }
    public DbGrabStatus(GrabStatus model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public bool Success { get; set; }

    public DateTime ModifiedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public GrabStatus ToModel()
        => new (new Symbol(Id)) {
            IsSuccessful = Success,
            ModifiedAt = ModifiedAt,
            Version = Version,
        };

    public void UpdateFrom(GrabStatus model)
    {
        this.RequireSameOrEmptyId(model.Id);

        if (!Id.IsNullOrEmpty())
            return;

        Id = model.Id;
        ModifiedAt = model.ModifiedAt;
        Version = model.Version;
    }
}
