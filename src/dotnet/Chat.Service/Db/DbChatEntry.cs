using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using ActualChat.Mathematics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;
namespace ActualChat.Chat.Db;

[Table("ChatEntries")]
[Index(nameof(ChatId), nameof(Id))]
[Index(
    nameof(ChatId),
    nameof(BeginsAt),
    nameof(EndsAt),
    nameof(Type))]
[IndexAttribute(
    nameof(ChatId),
    nameof(EndsAt),
    nameof(BeginsAt),
    nameof(Type))]
[IndexAttribute(nameof(ChatId), nameof(Version))]
public class DbChatEntry : IHasId<long>, IHasVersion<long>
{
    private DateTime _beginsAt;
    private DateTime? _endsAt;

    public DbChatEntry() { }

    public DbChatEntry(ChatEntry model) => UpdateFrom(model);

    [Key] public string CompositeId { get; set; } = "";
    public string ChatId { get; set; } = "";
    public long Id { get; set; }
    [ConcurrencyCheck] public long Version { get; set; }

    public string AuthorId { get; set; } = null!;

    public DateTime BeginsAt {
        get => _beginsAt.DefaultKind(DateTimeKind.Utc);
        set => _beginsAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? EndsAt {
        get => _endsAt?.DefaultKind(DateTimeKind.Utc);
        set => _endsAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public double Duration { get; set; }

    public ChatEntryType Type { get; set; }
    public string Content { get; set; } = "";
    public string? StreamId { get; set; }

    public long? AudioEntryId { get; set; }
    public long? VideoEntryId { get; set; }
    public string? TextToTimeMap { get; set; }

    public static string GetCompositeId(string chatId, long id)
        => $"{chatId}:{id.ToString(CultureInfo.InvariantCulture)}";

    public ChatEntry ToModel()
        => new() {
            Id = Id,
            ChatId = ChatId,
            AuthorId = AuthorId,
            BeginsAt = BeginsAt,
            EndsAt = EndsAt,
            Type = Type,
            Content = Content,
            StreamId = StreamId ?? "",
            AudioEntryId = AudioEntryId,
            VideoEntryId = VideoEntryId,
#pragma warning disable IL2026
            TextToTimeMap = TextToTimeMap != null
                ? JsonSerializer.Deserialize<LinearMap>(TextToTimeMap)
                : null,
#pragma warning restore IL2026
        };

    public void UpdateFrom(ChatEntry model)
    {
        if (model.Id == 0)
            throw new ArgumentOutOfRangeException(Invariant($"{nameof(model)}.{nameof(model.Id)}"));
        Id = model.Id;
        ChatId = model.ChatId;
        CompositeId = GetCompositeId(model.ChatId, model.Id);
        Version = model.Version;
        AuthorId = model.AuthorId;
        BeginsAt = model.BeginsAt;
        EndsAt = model.EndsAt;
        Type = model.Type;
        Content = model.Content;
        StreamId = model.StreamId;
        AudioEntryId = model.AudioEntryId;
        VideoEntryId = model.VideoEntryId;
#pragma warning disable IL2026
        TextToTimeMap = model.TextToTimeMap != null
            ? JsonSerializer.Serialize(model.TextToTimeMap)
            : null;
#pragma warning restore IL2026
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatEntry>
    {
        public void Configure(EntityTypeBuilder<DbChatEntry> builder)
        {
            builder.Property(x => x.AuthorId).IsRequired();
        }
    }
}
