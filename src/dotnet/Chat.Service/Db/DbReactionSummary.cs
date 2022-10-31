using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Stl.Versioning;

namespace ActualChat.Chat.Db;

[Table("ReactionSummaries")]
[Index(nameof(ChatEntryId))]
public class DbReactionSummary : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private static ITextSerializer<ImmutableList<string>> AuthorIdsSerializer { get; } =
        SystemJsonSerializer.Default.ToTyped<ImmutableList<string>>();

    [Key] public string Id { get; set; } = null!;
    public string ChatEntryId { get; set; } = "";
    public long Count { get; set; }
    public long Version { get; set; }
    public string Emoji { get; set; } = "";
    public string FirstAuthorIdsJson { get; set; } = "";

    public DbReactionSummary() { }
    public DbReactionSummary(ReactionSummary model) => UpdateFrom(model);

    public static string ComposeId(string chatEntryId, string emoji)
        => $"{chatEntryId}:{emoji}";

    public ReactionSummary ToModel()
        => new () {
            Id = Id,
            ChatEntryId = ChatEntryId,
            Emoji = Emoji,
            Count = Count,
            Version = Version,
            FirstAuthorIds = AuthorIdsSerializer.Read(FirstAuthorIdsJson),
        };

    public void UpdateFrom(ReactionSummary model)
    {
        Id = ComposeId(model.ChatEntryId, model.Emoji);
        ChatEntryId = model.ChatEntryId;
        Emoji = model.Emoji;
        Version = model.Version;
        Count = model.Count;
        FirstAuthorIdsJson = AuthorIdsSerializer.Write(model.FirstAuthorIds);
    }
}
