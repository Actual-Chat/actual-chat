using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;

namespace ActualChat.Search.Db;

[Table("IndexedChat")]
[Index(nameof(ChatCreatedAt))]
public class DbIndexedChat : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _chatCreatedAt;
    public DbIndexedChat() { }
    public DbIndexedChat(IndexedChat model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public long LastEntryLocalId { get; set; }
    public long LastEntryVersion { get; set; }

    public DateTime ChatCreatedAt {
        get => _chatCreatedAt.DefaultKind(DateTimeKind.Utc);
        set => _chatCreatedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public IndexedChat ToModel()
        => new (new ChatId(Id), Version) {
            LastEntryLocalId = LastEntryLocalId,
            LastEntryVersion = LastEntryVersion,
            ChatCreatedAt = ChatCreatedAt,
        };

    public void UpdateFrom(IndexedChat model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        LastEntryLocalId = model.LastEntryLocalId;
        LastEntryVersion = model.LastEntryVersion;
        ChatCreatedAt = model.ChatCreatedAt;
    }
}
