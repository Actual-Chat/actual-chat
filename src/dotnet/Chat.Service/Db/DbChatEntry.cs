using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Hashing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;
namespace ActualChat.Chat.Db;

[Table("ChatEntries")]
[Index(nameof(ChatId), nameof(Kind), nameof(LocalId), IsUnique = true)]
[Index(nameof(ChatId), nameof(Kind), nameof(IsRemoved), nameof(LocalId))] // For GetMaxLid queries
[Index(nameof(ChatId), nameof(Kind), nameof(BeginsAt), nameof(EndsAt))]
[Index(nameof(ChatId), nameof(Kind), nameof(EndsAt), nameof(BeginsAt))]
[Index(nameof(ChatId), nameof(Kind), nameof(Version))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbChatEntry : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    // SystemEntry payloads live in the Content column as JSON in the legacy wrapper shape
    // (LegacySystemEntry/LegacySystemEntryOption). This preserves the on-disk format —
    // existing rows deserialize without migration — while the in-memory model uses the new
    // SystemEntry hierarchy.
    private static ITextSerializer<LegacySystemEntry> LegacySystemEntrySerializer { get; } =
        Serializers.SystemJson.ToTyped<LegacySystemEntry>();

    public DbChatEntry() { }
    public DbChatEntry(ChatEntry model) => UpdateFrom(model);

    // (ChatId, Type, Id)
    [DbKey] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = "";
    public long LocalId { get; set; }

    public bool IsRemoved { get; set; }
    public string AuthorId { get; set; } = null!;
    public long? RepliedChatEntryId { get; set; }
    public bool IsSystemEntry { get; set; }
    public bool IsThreadStartEntry { get; set; }
    public bool IsThreadEntry { get; set; }
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
    public string? ContentStreamId { get; set; }

    public long? AudioEntryId { get; set; } // TODO(AY): Remove
    public string? AudioId { get; set; }
    public string? TimeMap { get; set; }
    public string? LocationId { get; set; }

    public ChatEntry ToModel(
        IEnumerable<ChatEntryAttachment>? attachments = null,
        LinkPreview[]? linkPreviews = null)
    {
        // fix NRE during deserialization of ApiArray at versions earlier than v0.200
        var attachmentsArray = attachments == null ? [] : attachments.OrderBy(x => x.Index).ToArray();
        var hasUploadingAttachments = attachmentsArray.Any(c => !c.Media.IsUploaded);
        var chatId = ActualChat.ChatId.Parse(ChatId);
        var id = ChatEntryId.New(chatId, LocalId);
        var linkPreviewIds = DeserializeLinkPreviewIds();
        linkPreviews ??= [];

        var flags = ChatEntryFlags.None;
        if (IsRemoved) flags |= ChatEntryFlags.IsRemoved;
        if (HasReactions) flags |= ChatEntryFlags.HasReactions;
        if (IsThreadStartEntry) flags |= ChatEntryFlags.IsThreadStart;
        if (IsThreadEntry) flags |= ChatEntryFlags.IsThread;
        if (hasUploadingAttachments) flags |= ChatEntryFlags.HasUploadingAttachments;

        // Build partial audio from DB columns
        var audio = BuildPartialAudio();

        ChatEntry baseEntry;
        if (IsSystemEntry) {
            var legacy = LegacySystemEntrySerializer.Read(Content);
            baseEntry = legacy.Option switch {
                LegacyMembersChangedOption mc => new MembersChangedEntry(id, Version) {
                    TargetAuthorId = mc.AuthorId,
                    TargetAuthorName = mc.AuthorName,
                    HasLeft = mc.HasLeft,
                },
                LegacyNotifyMembersOption nm => new NotifyMembersEntry(id, Version) {
                    TargetAuthorId = nm.AuthorId,
                    TargetAuthorName = nm.AuthorName,
                },
                _ => throw StandardError.Internal($"Unknown system entry option: {legacy.Option?.GetType().Name}"),
            };
        }
        else {
            baseEntry = new TextEntry(id, Version);
        }
        return baseEntry with {
            Flags = flags,
            AuthorId = ActualChat.AuthorId.Parse(AuthorId),
            ClientId = ClientId,
            BeginsAt = BeginsAt,
            EndsAt = EndsAt.ToMoment(),
            Content = !IsSystemEntry ? Content : "",
            ContentHash = new (ContentHash ?? ""),
            ContentStreamId = ContentStreamId ?? "",
            Audio = audio,
            LocationId = SharedLocationId.ParseNullable(LocationId),
            RepliedEntryLid = RepliedChatEntryId,
            Forwarded = BuildForwarded(),
            Attachments = attachmentsArray,
            LinkPreviewIds = linkPreviewIds,
            LinkPreviewMode = LinkPreviewMode ?? Media.LinkPreviewMode.Default,
            LinkPreviews = linkPreviews,
        };
    }

    private ChatEntryAudio? BuildPartialAudio()
    {
        if (AudioId.IsNullOrEmpty())
            return null;

        var timeMap = !TimeMap.IsNullOrEmpty()
            ? JsonSerializer.Deserialize<LinearMap>(TimeMap)
            : default;

        // MediaId column stores either a MediaId (parseable) or a stream ID (not parseable)
        return ActualChat.MediaId.TryParse(AudioId, out var mediaId)
            ? new ChatEntryAudio { MediaId = mediaId, TimeMap = timeMap, BeginsAt = BeginsAt }
            : new ChatEntryAudio { StreamId = AudioId, TimeMap = timeMap, BeginsAt = BeginsAt };
    }

    private ChatEntryForwarded? BuildForwarded()
    {
        if (ForwardedAuthorId.IsNullOrEmpty())
            return null;

        return new ChatEntryForwarded {
            ChatEntryId = ChatEntryId.ParseNullable(ForwardedChatEntryId),
            AuthorId = ActualChat.AuthorId.Parse(ForwardedAuthorId),
            BeginsAt = ForwardedChatEntryBeginsAt.ToMoment() ?? default,
            ChatTitle = ForwardedChatTitle ?? "",
            AuthorName = ForwardedAuthorName ?? "",
        };
    }

    public Symbol[] DeserializeLinkPreviewIds()
        => LinkPreviewIds.IsNullOrEmpty()
            ? []
            : JsonSerializer.Deserialize<Symbol[]>(LinkPreviewIds) ?? [];

    public void UpdateFrom(ChatEntry model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireVersion();
        if (!ClientId.IsNullOrEmpty())
            if (!model.ClientId.IsNullOrEmpty() && model.ClientId != ClientId)
                throw StandardError.Constraint("Client ID cannot be changed");

        Id = id.Value;
        ChatId = model.ChatId.Value;
        Kind = 0;
        LocalId = model.LocalId;
        Version = model.Version;
        IsRemoved = model.IsRemoved;
        IsThreadStartEntry = model.IsThreadStart;
        IsThreadEntry = model.IsThread;
        ClientId = model.ClientId;

        AuthorId = model.AuthorId.Value;
        BeginsAt = model.BeginsAt;
        EndsAt = model.EndsAt;
        Duration = EndsAt.HasValue ? (EndsAt.GetValueOrDefault() - BeginsAt).TotalSeconds : 0;
        HasReactions = model.HasReactions;
        ContentStreamId = model.ContentStreamId;
        RepliedChatEntryId = model.RepliedEntryLid;
        if (model.Forwarded is { } forwarded) {
            ForwardedChatEntryId = forwarded.ChatEntryId?.Value;
            ForwardedAuthorId = forwarded.AuthorId?.Value;
            ForwardedChatEntryBeginsAt = forwarded.BeginsAt;
            ForwardedChatTitle = forwarded.ChatTitle.NullIfEmpty();
            ForwardedAuthorName = forwarded.AuthorName.NullIfEmpty();
        }
        else {
            ForwardedChatEntryId = null;
            ForwardedAuthorId = null;
            ForwardedChatEntryBeginsAt = null;
            ForwardedChatTitle = null;
            ForwardedAuthorName = null;
        }
        IsSystemEntry = model is SystemEntry;
        if (model is SystemEntry sys) {
            Content = LegacySystemEntrySerializer.Write(ToLegacySystemEntry(sys));
            ContentHash = HashString.None;
        }
        else {
            Content = model.Content;
            ContentHash = model.GetContentHashString();
        }
        LinkPreviewIds = model.LinkPreviewIds.Length != 0
            ? JsonSerializer.Serialize(model.LinkPreviewIds)
            : null;

        // Write audio to DB columns (like SystemEntry → Content/IsSystemEntry)
        if (model.Audio is { } modelAudio) {
            var hasStreamId = !modelAudio.StreamId.IsNullOrEmpty();
            var hasMediaId = modelAudio.MediaId is { Value.Length: > 0 };
            Debug.Assert(hasStreamId ^ hasMediaId,
                "ChatEntryAudio must have either StreamId or MediaId set, but not both");
            AudioId = hasMediaId ? modelAudio.MediaId!.Value : modelAudio.StreamId;
            TimeMap = !modelAudio.TimeMap.IsEmpty
                ? JsonSerializer.Serialize(modelAudio.TimeMap)
                : null;
        }
        else {
            AudioId = null;
            TimeMap = null;
        }
        LocationId = model.LocationId?.Value;
    }

    private static LegacySystemEntry ToLegacySystemEntry(SystemEntry sys) => sys switch {
        MembersChangedEntry mc => new LegacySystemEntry {
            Option = new LegacyMembersChangedOption(mc.TargetAuthorId, mc.TargetAuthorName, mc.HasLeft),
        },
        NotifyMembersEntry nm => new LegacySystemEntry {
            Option = new LegacyNotifyMembersOption(nm.TargetAuthorId, nm.TargetAuthorName),
        },
        _ => throw StandardError.Internal($"Unknown system entry: {sys.GetType().Name}"),
    };

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
