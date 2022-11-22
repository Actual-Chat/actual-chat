using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Stl.Versioning;

namespace ActualChat.Chat.Db;

[Table("ReactionSummaries")]
[Index(nameof(EntryId))]
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

    public static string ComposeId(string chatEntryId, string emoji)
        => $"{chatEntryId}:{emoji}";

    public ReactionSummary ToModel()
        => new () {
            Id = Id,
            EntryId = EntryId,
            Emoji = Emoji,
            Count = Count,
            Version = Version,
            FirstAuthorIds = AuthorIdsSerializer.Read(FirstAuthorIdsJson),
        };

    public void UpdateFrom(ReactionSummary model)
    {
        Id = ComposeId(model.EntryId, model.Emoji);
        EntryId = model.EntryId;
        Emoji = model.Emoji;
        Version = model.Version;
        Count = model.Count;
        FirstAuthorIdsJson = AuthorIdsSerializer.Write(model.FirstAuthorIds);
    }
}
