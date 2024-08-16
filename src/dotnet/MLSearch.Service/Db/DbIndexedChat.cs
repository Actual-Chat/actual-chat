using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;

namespace ActualChat.Search.Db;

[Table("IndexedChat")]
[Index(nameof(ChatCreatedAt))]
public class DbIndexedChat : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private const string IndexSchemaVersionDelimiter = "-";
    public static readonly string IdIndexSchemaVersionPrefix = IndexNames.EntryIndexVersion + IndexSchemaVersionDelimiter;
    private DateTime _chatCreatedAt;
    public DbIndexedChat() { }
    public DbIndexedChat(IndexedChat model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = "";

    public ChatId GetChatId()
    {
        if (Id.IsNullOrEmpty())
            return ChatId.None;

        if (!Id.OrdinalStartsWith(IdIndexSchemaVersionPrefix))
            throw StandardError.Internal("Unexpected indexed chat id without correct version");

        return new ChatId(Id[IdIndexSchemaVersionPrefix.Length..]);
    }

    [ConcurrencyCheck] public long Version { get; set; }
    public long LastEntryLocalId { get; set; }
    public long LastEntryVersion { get; set; }

    public DateTime ChatCreatedAt {
        get => _chatCreatedAt.DefaultKind(DateTimeKind.Utc);
        set => _chatCreatedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public static string ComposeId(ChatId id)
        => IdIndexSchemaVersionPrefix + id;

    public IndexedChat ToModel()
        => new (GetChatId(), Version) {
            LastEntryLocalId = LastEntryLocalId,
            LastEntryVersion = LastEntryVersion,
            ChatCreatedAt = ChatCreatedAt,
        };

    public void UpdateFrom(IndexedChat model)
    {
        var id = ComposeId(model.Id);
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        LastEntryLocalId = model.LastEntryLocalId;
        LastEntryVersion = model.LastEntryVersion;
        ChatCreatedAt = model.ChatCreatedAt;
    }
}
