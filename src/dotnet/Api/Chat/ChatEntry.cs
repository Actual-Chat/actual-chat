using ActualChat.Comparison;
using ActualChat.Media;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatEntry(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] ChatEntryId Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] long Version = 0
    ) : IHasId<ChatEntryId>, IHasVersion<long>, IRequirementTarget
{
    public static readonly VersionEqualityComparer<ChatEntry, ChatEntryId> VersionEqualityComparer = new();
    public static readonly ChatEntry Loading = new(default, -1); // Should differ by Id & Version from None

    public static readonly Requirement<ChatEntry> MustExist = Requirement.New(
        (ChatEntry? c) => c is { Id.IsNone: false },
        new(() => StandardError.NotFound<ChatEntry>()));
    public static readonly Requirement<ChatEntry> MustNotBeRemoved = Requirement.New(
        (ChatEntry? c) => c is { Id.IsNone: false, IsRemoved: false },
        new(() => StandardError.NotFound<ChatEntry>()));

    public static ChatEntry Removed(ChatEntryId id)
        => new (id) { IsRemoved = true };

    #region MemoryPackXxx properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(13)]
    private ApiNullable8<Moment> MemoryPackClientSideBeginsAt {
        get => ClientSideBeginsAt;
        init => ClientSideBeginsAt = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(14)]
    private ApiNullable8<Moment> MemoryPackEndsAt {
        get => EndsAt;
        init => EndsAt = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(15)]
    private ApiNullable8<Moment> MemoryPackContentEndsAt {
        get => ContentEndsAt;
        init => ContentEndsAt = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(20)]
    private ApiNullable8<long> MemoryPackAudioEntryId {
        get => AudioEntryLid;
        init => AudioEntryLid = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(21)]
    private ApiNullable8<long> MemoryPackVideoEntryId {
        get => VideoEntryLid;
        init => VideoEntryLid = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(23)]
    private ApiNullable8<long> MemoryPackRepliedEntryLocalId {
        get => RepliedEntryLid;
        init => RepliedEntryLid = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(26)]
    private ApiNullable8<Moment> MemoryPackForwardedChatEntryBeginsAt {
        get => ForwardedChatEntryBeginsAt;
        init => ForwardedChatEntryBeginsAt = value;
    }

    #endregion

    [DataMember(Order = 10), MemoryPackOrder(10)] public bool IsRemoved { get; init; }
    [DataMember(Order = 11), MemoryPackOrder(11)] public AuthorId AuthorId { get; init; }
    [DataMember(Order = 12), MemoryPackOrder(12)] public Moment BeginsAt { get; init; }
    [DataMember(Order = 13), MemoryPackIgnore] public Moment? ClientSideBeginsAt { get; init; }
    [DataMember(Order = 14), MemoryPackIgnore] public Moment? EndsAt { get; init; }
    [DataMember(Order = 15), MemoryPackIgnore] public Moment? ContentEndsAt { get; init; }
    [DataMember(Order = 16), MemoryPackOrder(16)] public string Content { get; init; } = "";
    [DataMember(Order = 17), MemoryPackOrder(17)] public SystemEntry? SystemEntry { get; init; }
    [DataMember(Order = 18), MemoryPackOrder(18)] public bool HasReactions { get; init; }
    [DataMember(Order = 19), MemoryPackOrder(19)] public Symbol StreamId { get; init; } = "";
    [DataMember(Order = 20), MemoryPackIgnore] public long? AudioEntryLid { get; init; }
    [DataMember(Order = 21), MemoryPackIgnore] public long? VideoEntryLid { get; init; }
    [DataMember(Order = 22), MemoryPackOrder(22)] public LinearMap TimeMap { get; init; }
    [DataMember(Order = 23), MemoryPackIgnore] public long? RepliedEntryLid { get; init; }
    [DataMember(Order = 24), MemoryPackOrder(24)] public ChatEntryId ForwardedChatEntryId { get; init; }
    [DataMember(Order = 25), MemoryPackOrder(25)] public AuthorId ForwardedAuthorId { get; init; }
    [DataMember(Order = 26), MemoryPackIgnore] public Moment? ForwardedChatEntryBeginsAt { get; init; }
    [DataMember(Order = 27), MemoryPackOrder(27)] public string? ForwardedChatTitle { get; init; }
    [DataMember(Order = 28), MemoryPackOrder(28)] public string? ForwardedAuthorName { get; init; }
    [DataMember(Order = 29), MemoryPackOrder(29)] public Symbol LinkPreviewId { get; init; } = "";
    [DataMember(Order = 30), MemoryPackOrder(30)] public LinkPreviewMode LinkPreviewMode { get; init; }
    [DataMember(Order = 50), MemoryPackOrder(50)] public ApiArray<TextEntryAttachment> Attachments { get; init; }
    [DataMember(Order = 51), MemoryPackOrder(51)] public LinkPreview? LinkPreview { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public long LocalId => Id.LocalId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatEntryKind Kind => Id.Kind;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public double? Duration => EndsAt is { } endsAt ? (endsAt - BeginsAt).TotalSeconds : null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsStreaming => !StreamId.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsSystemEntry => SystemEntry != null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool HasAudioEntry => AudioEntryLid.HasValue;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool HasVideoEntry => VideoEntryLid.HasValue;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool HasMediaEntry => VideoEntryLid.HasValue || AudioEntryLid.HasValue;
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
    [DataMember, MemoryPackOrder(20)] public Option<long?> AudioEntryLid { get; init; }
    [DataMember, MemoryPackOrder(21)] public Option<long?> VideoEntryLid { get; init; }
    [DataMember, MemoryPackOrder(22)] public LinearMap? TimeMap { get; init; }
    [DataMember, MemoryPackOrder(23)] public Option<long?> RepliedEntryLid { get; init; }
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
        AudioEntryLid = entry.AudioEntryLid;
        VideoEntryLid = entry.VideoEntryLid;
        TimeMap = entry.TimeMap;
        RepliedEntryLid = entry.RepliedEntryLid;
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
