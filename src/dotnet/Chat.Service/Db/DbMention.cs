using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("Mentions")]
[Index(nameof(ChatId), nameof(EntryId), nameof(AuthorId))]
[Index(nameof(ChatId), nameof(AuthorId), nameof(EntryId))]
public class DbMention : IHasId<string>
{
    [Key] public string Id { get; set; } = null!;
    public string AuthorId { get; set; } = "";
    public string ChatId { get; set; } = "";
    public long EntryId { get; set; }

    public DbMention() { }
    public DbMention(Mention model) => UpdateFrom(model);

    public Mention ToModel()
        => new() {
            Id = Id,
            AuthorId = AuthorId,
            ChatId = ChatId,
            EntryId = EntryId,
        };

    public void UpdateFrom(Mention model)
    {
        Id = ComposeId(model.ChatId, model.EntryId, model.AuthorId);
        AuthorId = model.AuthorId;
        ChatId = model.ChatId;
        EntryId = model.EntryId;
    }

    public static string ComposeId(string chatId, long entryId, string authorId)
        => $"{chatId}:{entryId.ToString(CultureInfo.InvariantCulture)}:{authorId}";
}
