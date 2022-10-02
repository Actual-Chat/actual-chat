using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;
namespace ActualChat.Chat.Db;

[Table("ChatEntries")]
[Index(nameof(ChatId), nameof(Type), nameof(IsRemoved), nameof(Id))] // For GetEntryCount queries
[Index(nameof(ChatId), nameof(Type), nameof(Id))]
[Index(nameof(ChatId), nameof(Type), nameof(BeginsAt), nameof(EndsAt))]
[Index(nameof(ChatId), nameof(Type), nameof(EndsAt), nameof(BeginsAt))]
[Index(nameof(ChatId), nameof(Type), nameof(Version))]
public class DbChatEntry : IHasId<long>, IHasVersion<long>
{
    private DateTime _beginsAt;
    private DateTime? _clientSideBeginsAt;
    private DateTime? _endsAt;
    private DateTime? _contentEndsAt;

    public DbChatEntry() { }
    public DbChatEntry(ChatEntry model) => UpdateFrom(model);

    // (ChatId, Type, Id)
    [Key] public string CompositeId { get; set; } = "";
    public string ChatId { get; set; } = "";
    public long Id { get; set; }
    [ConcurrencyCheck] public long Version { get; set; }
    public bool IsRemoved { get; set; }
    public string AuthorId { get; set; } = null!;
    public long? RepliedChatEntryId { get; set; }

    public DateTime BeginsAt {
        get => _beginsAt.DefaultKind(DateTimeKind.Utc);
        set => _beginsAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? ClientSideBeginsAt {
        get => _clientSideBeginsAt?.DefaultKind(DateTimeKind.Utc);
        set => _clientSideBeginsAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? EndsAt {
        get => _endsAt?.DefaultKind(DateTimeKind.Utc);
        set => _endsAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? ContentEndsAt {
        get => _contentEndsAt?.DefaultKind(DateTimeKind.Utc);
        set => _contentEndsAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public double Duration { get; set; }

    public ChatEntryType Type { get; set; }
    public string Content { get; set; } = "";
    public bool HasAttachments { get; set; }
    public string? StreamId { get; set; }

    public long? AudioEntryId { get; set; }
    public long? VideoEntryId { get; set; }
    public string? TextToTimeMap { get; set; }

    public static string ComposeId(string chatId, ChatEntryType entryType, long entryId)
        => $"{chatId}:{entryType:D}:{entryId.ToString(CultureInfo.InvariantCulture)}";

    public ChatEntry ToModel(IEnumerable<TextEntryAttachment>? attachments = null)
        => new() {
            ChatId = ChatId,
            Type = Type,
            Id = Id,
            Version = Version,
            IsRemoved = IsRemoved,

            AuthorId = AuthorId,
            BeginsAt = BeginsAt,
            ClientSideBeginsAt = ClientSideBeginsAt,
            EndsAt = EndsAt,
            ContentEndsAt = ContentEndsAt,
            Content = Content,
            HasAttachments = HasAttachments,
            StreamId = StreamId ?? "",
            AudioEntryId = AudioEntryId,
            VideoEntryId = VideoEntryId,
            RepliedChatEntryId = RepliedChatEntryId!,
            Attachments = attachments?.ToImmutableArray() ?? ImmutableArray<TextEntryAttachment>.Empty,
#pragma warning disable IL2026
            TextToTimeMap = Type == ChatEntryType.Text
                ? TextToTimeMap != null
                ? JsonSerializer.Deserialize<LinearMap>(TextToTimeMap)
                : default
                : default,
#pragma warning restore IL2026
        };

    public void UpdateFrom(ChatEntry model)
    {
        CompositeId = ComposeId(model.ChatId, model.Type, model.Id);
        ChatId = model.ChatId;
        Type = model.Type;
        Id = model.Id;
        Version = model.Version;
        IsRemoved = model.IsRemoved;

        AuthorId = model.AuthorId;
        BeginsAt = model.BeginsAt;
        ClientSideBeginsAt = model.ClientSideBeginsAt;
        EndsAt = model.EndsAt;
        ContentEndsAt = model.ContentEndsAt;
        Duration = EndsAt.HasValue ? (EndsAt.GetValueOrDefault() - BeginsAt).TotalSeconds : 0;
        Content = model.Content;
        HasAttachments = model.HasAttachments;
        StreamId = model.StreamId;
        AudioEntryId = model.AudioEntryId;
        VideoEntryId = model.VideoEntryId;
        RepliedChatEntryId = model.RepliedChatEntryId;
#pragma warning disable IL2026
        TextToTimeMap = !model.TextToTimeMap.IsEmpty
            ? JsonSerializer.Serialize(model.TextToTimeMap)
            : null;
#pragma warning restore IL2026
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatEntry>
    {
        public void Configure(EntityTypeBuilder<DbChatEntry> builder)
            => builder.Property(x => x.AuthorId).IsRequired();
    }
}
