using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Media.Db;

// A row with no MediaId is a dismissal recorded before anything was generated.

[Table("ImageSuggestions")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbImageSuggestion : IHasId<string>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string? MediaId { get; set; }
    public string ImageDescription { get; set; } = "";

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? DismissedUntil {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value?.DefaultKind(DateTimeKind.Utc);
    }

    public ImageSuggestion? ToModel()
    {
        if (MediaId.IsNullOrEmpty())
            return null;

        return new ImageSuggestion(Id, ActualChat.MediaId.Parse(MediaId), ImageDescription, CreatedAt, Version);
    }
}
