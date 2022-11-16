using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("Mentions")]
[Index(nameof(ChatId), nameof(EntryId), nameof(AuthorId))]
[Index(nameof(ChatId), nameof(AuthorId), nameof(EntryId))]
public class DbMention : IHasId<string>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    public string ChatId { get; set; } = "";
    public string AuthorId { get; set; } = "";
    public long EntryId { get; set; }

    public DbMention() { }
    public DbMention(Mention model) => UpdateFrom(model);

    public static string ComposeId(ChatEntryId entryId, string authorId)
    {
        if (entryId.EntryKind != ChatEntryKind.Text)
            throw new ArgumentOutOfRangeException(nameof(entryId), "Only text entries support mentions.");
        return $"{entryId}:{authorId}";
    }

    public Mention ToModel()
        => new() {
            Id = Id,
            AuthorId = new AuthorId(AuthorId),
            EntryId = new ChatEntryId(new ChatId(ChatId), ChatEntryKind.Text, EntryId, Parse.None),
        };

    public void UpdateFrom(Mention model)
    {
        Id = ComposeId(model.EntryId, model.AuthorId);
        ChatId = model.ChatId;
        AuthorId = model.AuthorId;
        EntryId = model.EntryId.LocalId;
    }
}
