using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Hashing;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("Translations")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbTranslation : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string Content { get; set; } = "";
    public string SourceContentHash { get; set; } = "";
    public string StreamId { get; set; } = "";
    public string ChatId { get; set; } = "";
    public string EntryId { get; set; } = "";
    public bool IsRealtime { get; set; } // TODO(FC,AK): remove when stream api is unified

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
        => new(TranslationId.Parse(Id), Version) {
            Content = Content,
            SourceContentHash = new HashString(SourceContentHash),
            StreamId = StreamId,
            IsRealtime = IsRealtime,
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
        };

    public void UpdateFrom(Translation model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);

        Id = id.Value;
        var sourceId = id.SourceId;
        ChatId = sourceId.ChatId.Value;
        EntryId = sourceId.Kind is TranslationIdKind.TextEntry ? sourceId.GetChatEntryId().Value : "";
        Version = model.Version;
        Content = model.Content;
        SourceContentHash = model.SourceContentHash;
        StreamId = model.StreamId;
        IsRealtime = model.IsRealtime;
        CreatedAt = model.CreatedAt;
        ModifiedAt = model.ModifiedAt;
    }
}
