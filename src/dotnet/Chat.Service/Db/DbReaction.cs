using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

// Rows written before an author could leave several reactions on one entry carry the
// "{entryId}:{authorId}" id, so rows are looked up by (EntryId, AuthorId), never by a composed id.
[Table("Reactions")]
[Index(nameof(EntryId), nameof(AuthorId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbReaction : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string AuthorId { get; set; } = "";
    public string EntryId { get; set; } = "";
    public string Emoji { get; set; } = "";

    public DateTime ModifiedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public static string ComposeId(ChatEntryId entryId, AuthorId authorId, Emoji emoji)
        => $"{entryId}:{authorId}:{emoji.Value}";

    public DbReaction() { }
    public DbReaction(Reaction model) => UpdateFrom(model);

    public Reaction ToModel()
        => new () {
            Id = Id,
            Version = Version,
            AuthorId = ActualChat.AuthorId.Parse(AuthorId),
            EntryId = ChatEntryId.Parse(EntryId),
            Emoji = ActualChat.Emoji.Parse(Emoji),
            ModifiedAt = ModifiedAt,
        };

    public void UpdateFrom(Reaction model)
    {
        var id = ComposeId(model.EntryId, model.AuthorId, model.Emoji);
        this.RequireSameOrEmptyId(id);
        model.RequireVersion();

        Id = id;
        Version = model.Version;
        AuthorId = model.AuthorId.Value;
        EntryId = model.EntryId.Value;
        Emoji = model.Emoji.Value;
        ModifiedAt = model.ModifiedAt;
    }
}
