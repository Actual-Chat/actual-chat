using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("Mentions")]
[Index(nameof(ChatId), nameof(EntryLid), nameof(MentionId))]
[Index(nameof(ChatId), nameof(MentionId), nameof(EntryLid))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbMention : IHasId<string>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    public string ChatId { get; set; } = "";
    public string MentionId { get; set; } = "";
    public long EntryLid { get; set; }

    public DbMention() { }
    public DbMention(Mention model) => UpdateFrom(model);

    public static string ComposeId(ChatEntryId entryId, MentionId mentionId)
        => $"{entryId}:{mentionId}";

    public Mention ToModel()
        => new() {
            Id = Id,
            MentionId = ActualChat.MentionId.Parse(MentionId),
            EntryId = ChatEntryId.New(ActualChat.ChatId.Parse(ChatId), EntryLid),
        };

    public void UpdateFrom(Mention model)
    {
        var id = ComposeId(model.EntryId, model.MentionId);
        this.RequireSameOrEmptyId(id);

        Id = id;
        ChatId = model.ChatId.Value;
        MentionId = model.MentionId.Value;
        EntryLid = model.EntryId.LocalId;
    }
}
