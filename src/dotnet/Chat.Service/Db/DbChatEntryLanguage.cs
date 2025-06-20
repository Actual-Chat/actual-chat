using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Hashing;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("ChatEntryLanguages")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbChatEntryLanguage : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string Languages { get; set; } = "";
    public string EntryContentHash { get; set; } = "";

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime ModifiedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbChatEntryLanguage() { }
    public DbChatEntryLanguage(ChatEntryLanguage model) => UpdateFrom(model);

    public ChatEntryLanguage ToModel()
        => new (ChatEntryId.Parse(Id), Version) {
            Languages = Languages.IsNullOrEmpty()
                ? []
                : JsonSerializer.Deserialize<Language[]>(Languages) ?? [],
            EntryContentHash = new HashString(EntryContentHash),
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
        };

    public void UpdateFrom(ChatEntryLanguage model)
    {
        this.RequireSameOrEmptyId(model.Id.Value);
        model.RequireSomeVersion();

        Id = model.Id.Value;
        Version = model.Version;
        Languages = model.Languages.Length != 0
            ? JsonSerializer.Serialize(model.Languages)
            : "";
        EntryContentHash = model.EntryContentHash;
        CreatedAt = model.CreatedAt;
        ModifiedAt = model.ModifiedAt;
    }
}
