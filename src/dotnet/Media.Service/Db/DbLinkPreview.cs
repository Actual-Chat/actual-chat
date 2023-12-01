using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;

namespace ActualChat.Media.Db;

[Table("LinkPreviews")]
public class DbLinkPreview : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;
    private DateTime _modifiedAt;

    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string Url { get; set; } = "";
    public string ThumbnailMediaId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string MetadataJson { get; set; } = "";

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
            Version = Version,
            Url = Url,
            PreviewMediaId = new MediaId(ThumbnailMediaId),
            Title = Title,
            Description = Description,
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
            MetadataJson = MetadataJson,
        };

    public void UpdateFrom(LinkPreview model)
    {
        this.RequireSameOrEmptyId(model.Id);

        if (!Id.IsNullOrEmpty())
            return;

        Id = model.Id;
        Version = model.Version;
        Url = model.Url;
        ThumbnailMediaId = model.PreviewMediaId;
        Title = model.Title;
        Description = model.Description;
        CreatedAt = model.CreatedAt;
        ModifiedAt = model.ModifiedAt;
        MetadataJson = model.MetadataJson;
    }
}
