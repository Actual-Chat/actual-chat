using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;

namespace ActualChat.Chat.Db;

[Table("TextEntryAttachments")]
public class DbTextEntryAttachment : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    public DbTextEntryAttachment() { }
    public DbTextEntryAttachment(TextEntryAttachment model) => UpdateFrom(model);

    // (ChatId, EntryId, Index)
    string IHasId<string>.Id => CompositeId;
    [Key] public string CompositeId { get; set; } = "";
    public string ChatEntryId { get; set; } = "";
    public int Index { get; set; }
    [ConcurrencyCheck] public long Version { get; set; }
    public string ContentId { get; set; } = "";
    public string MetadataJson { get; set; } = "";

    public static string ComposeId(string chatId, long entryId, int index)
        => Invariant($"{chatId}:{entryId}:{index}");

    public TextEntryAttachment ToModel()
    {
        var chatEntryId = new ParsedChatEntryId(ChatEntryId);
        return new () {
            ChatId = chatEntryId.ChatId,
            EntryId = chatEntryId.EntryId,
            Index = Index,
            Version = Version,
            ContentId = ContentId,
            MetadataJson = MetadataJson
        };
    }

    public void UpdateFrom(TextEntryAttachment model)
    {
        CompositeId = ComposeId(model.ChatId, model.EntryId, model.Index);
        ChatEntryId = new ParsedChatEntryId(model.ChatId, ChatEntryType.Text, model.EntryId);
        Index = model.Index;
        Version = model.Version;
        ContentId = model.ContentId;
        MetadataJson = model.MetadataJson;
    }
}
