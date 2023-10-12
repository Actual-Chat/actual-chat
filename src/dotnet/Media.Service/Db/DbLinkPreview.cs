using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Chat;

namespace ActualChat.Media.Db;

[Table("LinkPreviews")]
public class DbLinkPreview : IHasId<string>, IRequirementTarget
{
    private DateTime _createdAt;
    private DateTime _modifiedAt;

    [Key] public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public string ThumbnailMediaId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime ModifiedAt {
        get => _modifiedAt.DefaultKind(DateTimeKind.Utc);
        set => _modifiedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbLinkPreview() { }
    public DbLinkPreview(LinkPreview model) => UpdateFrom(model);

    public LinkPreview ToModel()
        => new() {
            Id = new Symbol(Id),
            Url = Url,
            PreviewMediaId = new MediaId(ThumbnailMediaId),
            Title = Title,
            Description = Description,
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
        };

    public void UpdateFrom(LinkPreview model)
    {
        Id = model.Id;
        Url = model.Url;
        ThumbnailMediaId = model.PreviewMediaId;
        Title = model.Title;
        Description = model.Description;
        CreatedAt = model.CreatedAt;
        ModifiedAt = model.ModifiedAt;
    }
}
