using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;

namespace ActualChat.Chat.Db;

[Table("Reactions")]
public class DbReaction : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime? _modifiedAt;
    [Key] public string Id { get; set; } = null!;
    public string AuthorId { get; set; } = "";
    public string ChatEntryId { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string Emoji { get; set; } = "";

    public DateTime? ModifiedAt {
        get => _modifiedAt?.DefaultKind(DateTimeKind.Utc);
        set => _modifiedAt = value?.DefaultKind(DateTimeKind.Utc);
    }

    public DbReaction() { }
    public DbReaction(Reaction model) => UpdateFrom(model);

    public Reaction ToModel()
    {
        var chatEntryId = new ParsedChatEntryId(ChatEntryId);
        return new () {
            Id = Id,
            AuthorId = AuthorId,
            ChatId = chatEntryId.ChatId,
            EntryId = chatEntryId.EntryId,
            Emoji = Emoji,
        };
    }

    public void UpdateFrom(Reaction model)
    {
        var chatEntryId = new ParsedChatEntryId(model.ChatId, ChatEntryType.Text, model.EntryId);
        Id = ComposeId(chatEntryId, model.AuthorId);
        AuthorId = model.AuthorId;
        ChatEntryId = chatEntryId;
        Emoji = model.Emoji;
    }

    public static string ComposeId(string chatEntryId, string authorId)
        => $"{chatEntryId}:{authorId}";
}
