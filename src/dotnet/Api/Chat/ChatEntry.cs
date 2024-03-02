using ActualChat.Comparison;
using ActualChat.Media;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatEntry(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<ChatEntryId>, IHasVersion<long>, IRequirementTarget
{
    public static readonly VersionEqualityComparer<ChatEntry, ChatEntryId> VersionEqualityComparer = new();
    public static readonly ChatEntry Loading = new(default, -1); // Should differ by Id & Version from None

    public static readonly Requirement<ChatEntry> MustExist = Requirement.New(
        new(() => StandardError.NotFound<ChatEntry>()),
        (ChatEntry? c) => c is { Id.IsNone: false });
    public static readonly Requirement<ChatEntry> MustNotBeRemoved = Requirement.New(
        new(() => StandardError.NotFound<ChatEntry>()),
        (ChatEntry? c) => c is { Id.IsNone: false, IsRemoved: false });

    public static ChatEntry Removed(ChatEntryId id)
        => new (id) { IsRemoved = true };

    [DataMember, MemoryPackOrder(10)] public bool IsRemoved { get; init; }
    [DataMember, MemoryPackOrder(11)] public AuthorId AuthorId { get; init; }
    [DataMember, MemoryPackOrder(12)] public Moment BeginsAt { get; init; }
    [DataMember, MemoryPackOrder(13)] public Moment? ClientSideBeginsAt { get; init; }
    [DataMember, MemoryPackOrder(14)] public Moment? EndsAt { get; init; }
    [DataMember, MemoryPackOrder(15)] public Moment? ContentEndsAt { get; init; }
    [DataMember, MemoryPackOrder(16)] public string Content { get; init; } = "";
    [DataMember, MemoryPackOrder(17)] public SystemEntry? SystemEntry { get; init; }
    [DataMember, MemoryPackOrder(18)] public bool HasReactions { get; init; }
    [DataMember, MemoryPackOrder(19)] public Symbol StreamId { get; init; } = "";
    [DataMember, MemoryPackOrder(20)] public long? AudioEntryId { get; init; }
    [DataMember, MemoryPackOrder(21)] public long? VideoEntryId { get; init; }
    [DataMember, MemoryPackOrder(22)] public LinearMap TimeMap { get; init; }
    [DataMember, MemoryPackOrder(23)] public long? RepliedEntryLocalId { get; init; }
    [DataMember, MemoryPackOrder(24)] public ChatEntryId ForwardedChatEntryId { get; init; }
    [DataMember, MemoryPackOrder(25)] public AuthorId ForwardedAuthorId { get; init; }
    [DataMember, MemoryPackOrder(26)] public Moment? ForwardedChatEntryBeginsAt { get; init; }
    [DataMember, MemoryPackOrder(27)] public string? ForwardedChatTitle { get; init; }
    [DataMember, MemoryPackOrder(28)] public string? ForwardedAuthorName { get; init; }
    [DataMember, MemoryPackOrder(29)] public Symbol LinkPreviewId { get; init; } = "";
    [DataMember, MemoryPackOrder(30)] public LinkPreviewMode LinkPreviewMode { get; init; }
    [DataMember, MemoryPackOrder(50)] public ApiArray<TextEntryAttachment> Attachments { get; init; }
    [DataMember, MemoryPackOrder(51)] public LinkPreview? LinkPreview { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public long LocalId => Id.LocalId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatEntryKind Kind => Id.Kind;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public double? Duration => EndsAt is {} endsAt ? (endsAt - BeginsAt).TotalSeconds : null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsStreaming => !StreamId.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsSystemEntry => SystemEntry != null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool HasAudioEntry => AudioEntryId.HasValue;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool HasVideoEntry => VideoEntryId.HasValue;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool HasMediaEntry => VideoEntryId.HasValue || AudioEntryId.HasValue;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool HasMarkup => Kind == ChatEntryKind.Text && !IsSystemEntry && !HasMediaEntry;

    [MemoryPackConstructor]
    public ChatEntry() : this(ChatEntryId.None) { }

    // This record relies on referential equality
    public bool Equals(ChatEntry? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    public bool VersionEquals(ChatEntry? other) => VersionEqualityComparer.Equals(this, other);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method:MemoryPackConstructor]
public sealed partial record ChatEntryDiff() : RecordDiff
{
    [DataMember, MemoryPackOrder(10)] public bool? IsRemoved { get; init; }
    [DataMember, MemoryPackOrder(11)] public AuthorId? AuthorId { get; init; }
    [DataMember, MemoryPackOrder(12)] public Moment? BeginsAt { get; init; }
    [DataMember, MemoryPackOrder(13)] public Option<Moment?> ClientSideBeginsAt { get; init; }
    [DataMember, MemoryPackOrder(14)] public Option<Moment?> EndsAt { get; init; }
    [DataMember, MemoryPackOrder(15)] public Option<Moment?> ContentEndsAt { get; init; }
    [DataMember, MemoryPackOrder(16)] public string? Content { get; init; }
    [DataMember, MemoryPackOrder(17)] public Option<SystemEntry?> SystemEntry { get; init; }
    [DataMember, MemoryPackOrder(18)] public bool? HasReactions { get; init; }
    [DataMember, MemoryPackOrder(19)] public Symbol? StreamId { get; init; }
    [DataMember, MemoryPackOrder(20)] public Option<long?> AudioEntryId { get; init; }
    [DataMember, MemoryPackOrder(21)] public Option<long?> VideoEntryId { get; init; }
    [DataMember, MemoryPackOrder(22)] public LinearMap? TimeMap { get; init; }
    [DataMember, MemoryPackOrder(23)] public Option<long?> RepliedEntryLocalId { get; init; }
    [DataMember, MemoryPackOrder(24)] public ChatEntryId? ForwardedChatEntryId { get; init; }
    [DataMember, MemoryPackOrder(25)] public AuthorId? ForwardedAuthorId { get; init; }
    [DataMember, MemoryPackOrder(26)] public Option<Moment?> ForwardedChatEntryBeginsAt { get; init; }
    [DataMember, MemoryPackOrder(27)] public string? ForwardedChatTitle { get; init; }
    [DataMember, MemoryPackOrder(28)] public string? ForwardedAuthorName { get; init; }
    [DataMember, MemoryPackOrder(29)] public Symbol? LinkPreviewId { get; init; }
    [DataMember, MemoryPackOrder(30)] public LinkPreviewMode? LinkPreviewMode { get; init; }
    [DataMember, MemoryPackOrder(50)] public ApiArray<TextEntryAttachment>? Attachments { get; init; }

    public ChatEntryDiff(ChatEntry entry) : this()
    {
        IsRemoved = entry.IsRemoved;
        AuthorId = entry.AuthorId;
        BeginsAt = entry.BeginsAt;
        ClientSideBeginsAt = entry.ClientSideBeginsAt;
        EndsAt = entry.EndsAt;
        ContentEndsAt = entry.ContentEndsAt;
        Content = entry.Content;
        SystemEntry = entry.SystemEntry;
        HasReactions = entry.HasReactions;
        StreamId = entry.StreamId;
        AudioEntryId = entry.AudioEntryId;
        VideoEntryId = entry.VideoEntryId;
        TimeMap = entry.TimeMap;
        RepliedEntryLocalId = entry.RepliedEntryLocalId;
        ForwardedChatEntryId = entry.ForwardedChatEntryId;
        ForwardedAuthorId = entry.ForwardedAuthorId;
        ForwardedChatEntryBeginsAt = entry.ForwardedChatEntryBeginsAt;
        ForwardedChatTitle = entry.ForwardedChatTitle;
        ForwardedAuthorName = entry.ForwardedAuthorName;
        LinkPreviewId = entry.LinkPreviewId;
        LinkPreviewMode = entry.LinkPreviewMode;
        Attachments = entry.Attachments;
    }
}
