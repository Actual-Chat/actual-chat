using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Hashing;
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
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbChatEntry : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private static ITextSerializer<SystemEntry> SystemEntrySerializer { get; } =
        SystemJsonSerializer.Default.ToTyped<SystemEntry>();

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
    public bool IsThreadStartEntry { get; set; }
    public bool IsThreadEntry { get; set; }
    public bool HasAttachmentUploads { get; set; }
    public string ClientId { get; set; } = "";

    public string? ForwardedChatTitle { get; set; }
    public string? ForwardedAuthorId { get; set; }
    public string? ForwardedAuthorName { get; set; }
    public string? ForwardedChatEntryId { get; set; }
    public DateTime? ForwardedChatEntryBeginsAt { get; set; }

    public DateTime BeginsAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? ClientSideBeginsAt {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? EndsAt {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? ContentEndsAt {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public double Duration { get; set; }

    public int Kind { get; set; }
    public string Content { get; set; } = "";
    public string? ContentHash { get; set; } = "";
    public bool HasAttachments { get; set; }
    public bool HasReactions { get; set; }
    public string? LinkPreviewIds { get; set; }
    public LinkPreviewMode? LinkPreviewMode { get; set; }
    public string? StreamId { get; set; }

    public long? AudioEntryId { get; set; }
    public long? VideoEntryId { get; set; }
    public string? MediaId { get; set; }
    public string? TimeMap { get; set; }

    public ChatEntry ToModel(
        IEnumerable<TextEntryAttachment>? attachments = null,
        LinkPreview[]? linkPreviews = null)
    {
        // fix NRE during deserialization of ApiArray at versions earlier than v0.200
        var attachmentsArray = attachments == null ? [] : attachments.OrderBy(x => x.Index).ToArray();
        var hasAttachmentUploads = attachmentsArray.Any(c => c.Media.BlobId.IsNullOrEmpty());
        var chatId = ActualChat.ChatId.Parse(ChatId);
        var id = ChatEntryId.New(chatId, LocalId);
        var linkPreviewIds = DeserializeLinkPreviewIds();
        linkPreviews ??= [];

        var flags = ChatEntryFlags.None;
        if (IsRemoved) flags |= ChatEntryFlags.IsRemoved;
        if (HasReactions) flags |= ChatEntryFlags.HasReactions;
        if (IsThreadStartEntry) flags |= ChatEntryFlags.IsThreadStart;
        if (IsThreadEntry) flags |= ChatEntryFlags.IsThread;
        if (HasAttachmentUploads || hasAttachmentUploads) flags |= ChatEntryFlags.HasAttachmentUploads;

        // Build partial audio from DB columns (like SystemEntry pattern)
        var audio = BuildPartialAudio();

        return new (id, Version) {
            Flags = flags,
            AuthorId = ActualChat.AuthorId.Parse(AuthorId),
            ClientId = ClientId,
            BeginsAt = BeginsAt,
            EndsAt = EndsAt.ToMoment(),
            Content = !IsSystemEntry ? Content : "",
            ContentHash = new (ContentHash ?? ""),
            SystemEntry = IsSystemEntry ? SystemEntrySerializer.Read(Content) : null,
            StreamId = StreamId ?? "",
            Audio = audio,
            RepliedEntryLid = RepliedChatEntryId,
            ForwardedChatTitle = ForwardedChatTitle,
            ForwardedAuthorId = ActualChat.AuthorId.ParseNullable(ForwardedAuthorId),
            ForwardedAuthorName = ForwardedAuthorName,
            ForwardedChatEntryId = ChatEntryId.ParseNullable(ForwardedChatEntryId),
            ForwardedChatEntryBeginsAt = ForwardedChatEntryBeginsAt.ToMoment(),
            Attachments = !hasAttachmentUploads ? attachmentsArray : [],
            AttachmentUploads = hasAttachmentUploads ? attachmentsArray : [],
            LinkPreviewIds = linkPreviewIds,
            LinkPreviewMode = LinkPreviewMode ?? Media.LinkPreviewMode.Default,
            LinkPreviews = linkPreviews,
        };
    }

    private ChatEntryAudio? BuildPartialAudio()
    {
        if (MediaId.IsNullOrEmpty())
            return null;

        var timeMap = !TimeMap.IsNullOrEmpty()
            ? JsonSerializer.Deserialize<LinearMap>(TimeMap)
            : default;

        // MediaId column stores either a MediaId (parseable) or a stream ID (not parseable)
        return ActualChat.MediaId.TryParse(MediaId, out var mediaId)
            ? new ChatEntryAudio { MediaId = mediaId, TimeMap = timeMap, BeginsAt = BeginsAt }
            : new ChatEntryAudio { StreamId = MediaId, TimeMap = timeMap, BeginsAt = BeginsAt };
    }

    public Symbol[] DeserializeLinkPreviewIds()
        => LinkPreviewIds.IsNullOrEmpty()
            ? []
            : JsonSerializer.Deserialize<Symbol[]>(LinkPreviewIds) ?? [];

    public void UpdateFrom(ChatEntry model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireSomeVersion();
        if (!ClientId.IsNullOrEmpty())
            if (!model.ClientId.IsNullOrEmpty() && !OrdinalEquals(model.ClientId, ClientId))
                throw StandardError.Constraint("Client ID cannot be changed");

        Id = id.Value;
        ChatId = model.ChatId.Value;
        Kind = 0;
        LocalId = model.LocalId;
        Version = model.Version;
        IsRemoved = model.IsRemoved;
        IsThreadStartEntry = model.IsThreadStart;
        IsThreadEntry = model.IsThread;
        HasAttachmentUploads = model.HasAttachmentUploads;
        ClientId = model.ClientId;

        AuthorId = model.AuthorId.Value;
        BeginsAt = model.BeginsAt;
        EndsAt = model.EndsAt;
        Duration = EndsAt.HasValue ? (EndsAt.GetValueOrDefault() - BeginsAt).TotalSeconds : 0;
        HasReactions = model.HasReactions;
        StreamId = model.StreamId;
        RepliedChatEntryId = model.RepliedEntryLid;
        ForwardedChatTitle = model.ForwardedChatTitle;
        ForwardedAuthorId = model.ForwardedAuthorId?.Value;
        ForwardedAuthorName = model.ForwardedAuthorName;
        ForwardedChatEntryId = model.ForwardedChatEntryId?.Value;
        ForwardedChatEntryBeginsAt = model.ForwardedChatEntryBeginsAt;
        Content = model.SystemEntry != null ? SystemEntrySerializer.Write(model.SystemEntry) : model.Content;
        ContentHash = model.SystemEntry != null ? HashString.None : model.GetContentHashString();
        IsSystemEntry = model.SystemEntry != null;
        LinkPreviewIds = model.LinkPreviewIds.Length != 0
            ? JsonSerializer.Serialize(model.LinkPreviewIds)
            : null;

        // Write audio to DB columns (like SystemEntry → Content/IsSystemEntry)
        if (model.Audio is { } modelAudio) {
            var hasStreamId = !modelAudio.StreamId.IsNullOrEmpty();
            var hasMediaId = modelAudio.MediaId is { Value.Length: > 0 };
            Debug.Assert(hasStreamId ^ hasMediaId,
                "ChatEntryAudio must have either StreamId or MediaId set, but not both");
            MediaId = hasMediaId ? modelAudio.MediaId!.Value : modelAudio.StreamId;
            TimeMap = !modelAudio.TimeMap.IsEmpty
                ? JsonSerializer.Serialize(modelAudio.TimeMap)
                : null;
        }
        else {
            MediaId = null;
            TimeMap = null;
        }
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatEntry>
    {
        public void Configure(EntityTypeBuilder<DbChatEntry> builder)
        {
            builder.Property(x => x.AuthorId).IsRequired();

            builder.HasIndex(nameof(ChatId), nameof(Version), nameof(LocalId))
                .HasFilter("kind = 0");
        }
    }
}
