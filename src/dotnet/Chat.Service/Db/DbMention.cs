using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("Mentions")]
[Index(nameof(ChatId), nameof(EntryLid), nameof(MentionRef))]
[Index(nameof(ChatId), nameof(MentionRef), nameof(EntryLid))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbMention : IHasId<string>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    public string ChatId { get; set; } = "";
    [Column("mention_id")] public string MentionRef { get; set; } = "";
    public long EntryLid { get; set; }

    public DbMention() { }
    public DbMention(Mention model) => UpdateFrom(model);

    public static string ComposeId(ChatEntryId entryId, MentionRef mentionId)
        => $"{entryId}:{mentionId}";

    public Mention ToModel()
        => new() {
            Id = Id,
            MentionRef = ActualChat.MentionRef.Parse(MentionRef),
            EntryId = ChatEntryId.New(ActualChat.ChatId.Parse(ChatId), EntryLid),
        };

    public void UpdateFrom(Mention model)
    {
        var id = ComposeId(model.EntryId, model.MentionRef);
        this.RequireSameOrEmptyId(id);

        Id = id;
        ChatId = model.ChatId.Value;
        MentionRef = model.MentionRef.Value;
        EntryLid = model.EntryId.LocalId;
    }
}
