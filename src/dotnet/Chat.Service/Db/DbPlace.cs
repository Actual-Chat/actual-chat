using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("Places")]
[Index(nameof(CreatedAt))]
[Index(nameof(Version), nameof(Id))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbPlace : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    public DbPlace() { }
    public DbPlace(Place model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string MediaId { get; set; } = "";
    public string BackgroundMediaId { get; set; } = "";
    [Column("user_link_id")] // TODO(AY): Rename to alias_id
    public string AliasId { get; set; } = "";

    // Permissions & Rules
    public bool IsPublic { get; set; }

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public Place ToModel()
        => new(PlaceId.Parse(Id), Version) {
            Title = Title,
            Description = Description,
            CreatedAt = CreatedAt,
            IsPublic = IsPublic,
            MediaId = ActualChat.MediaId.ParseNullable(MediaId),
            BackgroundMediaId = ActualChat.MediaId.ParseNullable(BackgroundMediaId),
            AliasId = ActualChat.AliasId.ParseNullable(AliasId),
        };

    public void UpdateFrom(Place model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireSomeVersion();

        Id = id.Value;
        Version = model.Version;
        Title = model.Title;
        Description = model.Description;
        CreatedAt = model.CreatedAt;
        IsPublic = model.IsPublic;
        MediaId = model.MediaId?.Value ?? "";
        BackgroundMediaId = model.BackgroundMediaId?.Value ?? "";
        AliasId = model.AliasId?.NormalizedValue ?? "";
    }
}
