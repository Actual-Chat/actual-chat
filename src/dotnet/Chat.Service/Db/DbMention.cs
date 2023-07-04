using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("Mentions")]
[Index(nameof(ChatId), nameof(EntryId), nameof(MentionId))]
[Index(nameof(ChatId), nameof(MentionId), nameof(EntryId))]
public class DbMention : IHasId<string>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    public string ChatId { get; set; } = "";
    public string MentionId { get; set; } = "";
    public long EntryId { get; set; }

    public DbMention() { }
    public DbMention(Mention model) => UpdateFrom(model);

    public static string ComposeId(ChatEntryId entryId, MentionId mentionId)
    {
        if (entryId.Kind != ChatEntryKind.Text)
            throw new ArgumentOutOfRangeException(nameof(entryId), "Only text entries support mentions.");

        return $"{entryId}:{mentionId}";
    }

    public Mention ToModel()
        => new() {
            Id = Id,
            MentionId = new MentionId(MentionId),
            EntryId = new ChatEntryId(new ChatId(ChatId), ChatEntryKind.Text, EntryId, AssumeValid.Option),
        };

    public void UpdateFrom(Mention model)
    {
        var id = ComposeId(model.EntryId, model.MentionId);
        this.RequireSameOrEmptyId(id);

        Id = id;
        ChatId = model.ChatId;
        MentionId = model.MentionId;
        EntryId = model.EntryId.LocalId;
    }
}
