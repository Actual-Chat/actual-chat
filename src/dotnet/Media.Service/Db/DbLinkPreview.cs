using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;

namespace ActualChat.Media.Db;

[Table("LinkPreviews")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbLinkPreview : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string Url { get; set; } = "";
    public string ThumbnailMediaId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string MetadataJson { get; set; } = "";

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime ModifiedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbLinkPreview() { }
    public DbLinkPreview(LinkPreview model) => UpdateFrom(model);

    public LinkPreview ToModel()
        => new() {
            Id = new Symbol(Id),
            Version = Version,
            Url = Url,
            PreviewMediaId = MediaId.ParseNullable(ThumbnailMediaId),
            Title = Title,
            Description = Description,
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
            Metadata = MetadataBagJson.FromJson(MetadataJson),
        };

    public void UpdateFrom(LinkPreview model)
    {
        this.RequireSameOrEmptyId(model.Id);

        Id = model.Id;
        Version = model.Version;
        Url = model.Url;
        ThumbnailMediaId = model.PreviewMediaId?.Value ?? "";
        Title = model.Title;
        Description = model.Description;
        CreatedAt = model.CreatedAt;
        ModifiedAt = model.ModifiedAt;
        MetadataJson = model.Metadata.ToJson();
    }
}
