using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Hashing;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("Translations")]
public class DbTranslation : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string Content { get; set; } = "";
    public string SourceContentHash { get; set; } = "";

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime ModifiedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbTranslation() { }
    public DbTranslation(Translation model) => UpdateFrom(model);

    public Translation ToModel()
        => new(new TranslationId(Id), Version) {
            Content = Content,
            SourceContentHash = new HashString(SourceContentHash),
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
        };

    public void UpdateFrom(Translation model)
    {
        this.RequireSameOrEmptyId(model.Id);

        Id = model.Id;
        Version = model.Version;
        Content = model.Content;
        SourceContentHash = model.SourceContentHash;
        CreatedAt = model.CreatedAt;
        ModifiedAt = model.ModifiedAt;
    }
}
