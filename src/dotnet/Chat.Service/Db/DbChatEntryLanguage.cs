using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Chat.Db;

[Table("ChatEntryLanguages")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbChatEntryLanguage : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string Languages { get; set; } = "";

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
        => new (new ChatEntryId(Id), Version) {
            Languages = Languages.IsNullOrEmpty()
                ? ApiArray<Language>.Empty
                : JsonSerializer.Deserialize<ApiArray<Language>>(Languages) ?? ApiArray<Language>.Empty,
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
        };

    public void UpdateFrom(ChatEntryLanguage model)
    {
        this.RequireSameOrEmptyId(model.Id);
        model.RequireSomeVersion();

        Id = model.Id;
        Version = model.Version;
        Languages = !model.Languages.IsEmpty ? JsonSerializer.Serialize(model.Languages) : "";
        CreatedAt = model.CreatedAt;
        ModifiedAt = model.ModifiedAt;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatEntryLanguage>
    {
        public void Configure(EntityTypeBuilder<DbChatEntryLanguage> builder)
            => builder.HasIndex(nameof(Id)).HasFilter("languages = ''");
    }
}
