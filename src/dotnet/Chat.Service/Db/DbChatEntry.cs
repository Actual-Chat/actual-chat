using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;
namespace ActualChat.Chat.Db;

[Table("ChatEntries")]
[Index(nameof(ChatId), nameof(Kind), nameof(LocalId), IsUnique = true)]
[Index(nameof(ChatId), nameof(Kind), nameof(IsRemoved), nameof(LocalId))] // For GetEntryCount queries
[Index(nameof(ChatId), nameof(Kind), nameof(BeginsAt), nameof(EndsAt))]
[Index(nameof(ChatId), nameof(Kind), nameof(EndsAt), nameof(BeginsAt))]
[Index(nameof(ChatId), nameof(Kind), nameof(Version))]
public class DbChatEntry : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private static ITextSerializer<SystemEntry> SystemEntrySerializer { get; } =
        SystemJsonSerializer.Default.ToTyped<SystemEntry>();
    private DateTime _beginsAt;
    private DateTime? _clientSideBeginsAt;
    private DateTime? _endsAt;
    private DateTime? _contentEndsAt;

    public DbChatEntry() { }
    public DbChatEntry(ChatEntry model) => UpdateFrom(model);

    // (ChatId, Type, Id)
    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = "";
    public long LocalId { get; set; }

    public bool IsRemoved { get; set; }
    public string AuthorId { get; set; } = null!;
    public long? RepliedChatEntryId { get; set; }
    public bool IsSystemEntry { get; set; }
    public bool IsSearchResult { get; set; }
    public bool IsSearchSummary { get; set; }

    public string? ForwardedChatTitle { get; set; }
    public string? ForwardedAuthorId { get; set; }
    public string? ForwardedAuthorName { get; set; }
    public string? ForwardedChatEntryId { get; set; }
    public DateTime? ForwardedChatEntryBeginsAt { get; set; }

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

    public ChatEntryKind Kind { get; set; }
    public string Content { get; set; } = "";
    public bool HasAttachments { get; set; }
    public bool HasReactions { get; set; }
    public string? LinkPreviewId { get; set; }
    public LinkPreviewMode? LinkPreviewMode { get; set; }
    public string? StreamId { get; set; }

    public long? AudioEntryId { get; set; }
    public long? VideoEntryId { get; set; }
    public string? TimeMap { get; set; }

    public ChatEntry ToModel(IEnumerable<TextEntryAttachment>? attachments = null, Media.LinkPreview? linkPreview = null)
    {
        // fix NRE during deserialization of ApiArray at versions earlier than v0.200
        var attachmentsArray = attachments == null
            ? new ApiArray<TextEntryAttachment>(Array.Empty<TextEntryAttachment>())
            : new ApiArray<TextEntryAttachment>(attachments!.OrderBy(x => x.Index).ToArray());
        var chatId = new ChatId(ChatId);
        var id = new ChatEntryId(Id, chatId, Kind, LocalId, AssumeValid.Option);
        return new (id, Version) {
            IsRemoved = IsRemoved,
            AuthorId = new AuthorId(AuthorId),
            BeginsAt = BeginsAt,
            ClientSideBeginsAt = ClientSideBeginsAt,
            EndsAt = EndsAt,
            ContentEndsAt = ContentEndsAt,
            Content = !IsSystemEntry ? Content : "",
            SystemEntry = IsSystemEntry ? SystemEntrySerializer.Read(Content) : null,
            HasReactions = HasReactions,
            StreamId = StreamId ?? "",
            AudioEntryId = AudioEntryId,
            VideoEntryId = VideoEntryId,
            RepliedEntryLocalId = RepliedChatEntryId!,
            ForwardedChatTitle = ForwardedChatTitle,
            ForwardedAuthorId = new AuthorId(ForwardedAuthorId),
            ForwardedAuthorName = ForwardedAuthorName,
            ForwardedChatEntryId = new ChatEntryId(ForwardedChatEntryId),
            ForwardedChatEntryBeginsAt = ForwardedChatEntryBeginsAt,
            Attachments = attachmentsArray,
            LinkPreviewId = LinkPreviewId,
            LinkPreviewMode = LinkPreviewMode ?? Media.LinkPreviewMode.Default,
            LinkPreview = linkPreview,
            TimeMap = Kind == ChatEntryKind.Text
                ? TimeMap != null
                    ? JsonSerializer.Deserialize<LinearMap>(TimeMap)
                    : default
                : default,
            IsSearchResult = IsSearchResult,
            IsSearchSummary = IsSearchSummary,
        };
    }

    public void UpdateFrom(ChatEntry model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        ChatId = model.ChatId;
        Kind = model.Kind;
        LocalId = model.LocalId;
        Version = model.Version;
        IsRemoved = model.IsRemoved;

        AuthorId = model.AuthorId;
        BeginsAt = model.BeginsAt;
        ClientSideBeginsAt = model.ClientSideBeginsAt;
        EndsAt = model.EndsAt;
        ContentEndsAt = model.ContentEndsAt;
        Duration = EndsAt.HasValue ? (EndsAt.GetValueOrDefault() - BeginsAt).TotalSeconds : 0;
        HasReactions = model.HasReactions;
        StreamId = model.StreamId;
        AudioEntryId = model.AudioEntryId;
        VideoEntryId = model.VideoEntryId;
        RepliedChatEntryId = model.RepliedEntryLocalId;
        ForwardedChatTitle = model.ForwardedChatTitle;
        ForwardedAuthorId = model.ForwardedAuthorId;
        ForwardedAuthorName = model.ForwardedAuthorName;
        ForwardedChatEntryId = model.ForwardedChatEntryId;
        ForwardedChatEntryBeginsAt = model.ForwardedChatEntryBeginsAt;
        Content = model.SystemEntry != null ? SystemEntrySerializer.Write(model.SystemEntry) : model.Content;
        IsSystemEntry = model.SystemEntry != null;
        LinkPreviewId = model.LinkPreviewId;
        LinkPreviewMode = model.LinkPreviewMode;
        TimeMap = !model.TimeMap.IsEmpty
            ? JsonSerializer.Serialize(model.TimeMap)
            : null;
        IsSearchResult = model.IsSearchResult;
        IsSearchSummary = model.IsSearchSummary;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatEntry>
    {
        public void Configure(EntityTypeBuilder<DbChatEntry> builder)
            => builder.Property(x => x.AuthorId).IsRequired();
    }
}
