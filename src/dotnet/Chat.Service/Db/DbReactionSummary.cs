using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("ReactionSummaries")]
[Index(nameof(EntryId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbReactionSummary : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private static ITextSerializer<ImmutableList<AuthorId>> AuthorIdsSerializer { get; } =
        SystemJsonSerializer.Default.ToTyped<ImmutableList<AuthorId>>();

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string EntryId { get; set; } = "";

    public long Count { get; set; }
    public string Emoji { get; set; } = "";
    public string FirstAuthorIdsJson { get; set; } = "";

    public DbReactionSummary() { }
    public DbReactionSummary(ReactionSummary model) => UpdateFrom(model);

    public static string ComposeId(ChatEntryId entryId, string emojiId)
        => $"{entryId}:{emojiId}";

    public ReactionSummary ToModel()
        => new () {
            Id = Id,
            EntryId = TextEntryId.Parse(EntryId),
            Emoji = ActualChat.Emoji.Parse(Emoji),
            Count = Count,
            Version = Version,
            FirstAuthorIds = AuthorIdsSerializer.Read(FirstAuthorIdsJson),
        };

    public void UpdateFrom(ReactionSummary model)
    {
        var id = ComposeId(model.EntryId, model.Emoji.Value);
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        EntryId = model.EntryId.Value;
        Emoji = model.Emoji.Value;
        Version = model.Version;
        Count = model.Count;
        FirstAuthorIdsJson = AuthorIdsSerializer.Write(model.FirstAuthorIds);
    }
}
