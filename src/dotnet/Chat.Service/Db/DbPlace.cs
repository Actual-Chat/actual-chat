using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("Places")]
[Index(nameof(CreatedAt))]
[Index(nameof(Version), nameof(Id))]
public class DbPlace : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;

    public DbPlace() { }
    public DbPlace(Place model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string Title { get; set; } = "";
    public string MediaId { get; set; } = "";
    public string BackgroundMediaId { get; set; } = "";

    // Permissions & Rules
    public bool IsPublic { get; set; }

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public Place ToModel()
        => new(new PlaceId(Id), Version) {
            Title = Title,
            CreatedAt = CreatedAt,
            IsPublic = IsPublic,
            MediaId = new MediaId(MediaId),
            BackgroundMediaId = new MediaId(BackgroundMediaId)
        };

    public void UpdateFrom(Place model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        Title = model.Title;
        CreatedAt = model.CreatedAt;
        IsPublic = model.IsPublic;
        MediaId = model.MediaId;
        BackgroundMediaId = model.BackgroundMediaId;
    }
}
