namespace ActualChat.Live;

/// <summary>
/// The <see cref="LiveSessionState"/> wire shape of 2.21 and older. Key 25 is retired; 22 and 23 are
/// retired too but stay on the wire, as 2.20-2.21 read them as an enum and a bool and can't take a nil gap.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record LegacyLiveSessionState
{
    [DataMember(Order = 0), Key(0)]
    public ChatId ChatId { get; init; } = null!;
    [DataMember(Order = 1), Key(1)]
    public long StartEntryLid { get; init; }
    [DataMember(Order = 2), Key(2)]
    public long EndEntryLid { get; init; }
    [DataMember(Order = 3), Key(3)]
    public Moment StartedAt { get; init; }
    [DataMember(Order = 4), Key(4)]
    public IReadOnlyList<AuthorId> AuthorIds { get; init; } = [];
    [DataMember(Order = 5), Key(5)]
    public string Title { get; init; } = "";
    [DataMember(Order = 6), Key(6)]
    public string Description { get; init; } = "";
    [DataMember(Order = 7), Key(7)]
    public string Summary { get; init; } = "";
    [DataMember(Order = 8), Key(8)]
    public bool TranscriptionOn { get; init; }
    [DataMember(Order = 9), Key(9)]
    public int MessageCount { get; init; }
    [DataMember(Order = 10), Key(10)]
    public Moment LastSummaryAt { get; init; }
    [DataMember(Order = 11), Key(11)]
    public bool StartNotificationSent { get; init; }
    [DataMember(Order = 12), Key(12)]
    public long Version { get; init; }
    [DataMember(Order = 13), Key(13)]
    public bool IsClosing { get; init; }
    [DataMember(Order = 14), Key(14)]
    public Moment? ClosingAt { get; init; }
    [DataMember(Order = 15), Key(15)]
    public AuthorId Host { get; init; } = null!;
    [DataMember(Order = 16), Key(16)]
    public SessionRules Rules { get; init; } = SessionRules.Default;
    [DataMember(Order = 17), Key(17)]
    public Moment? SessionStartedAt { get; init; }
    [DataMember(Order = 18), Key(18)]
    public LiveSessionKind Kind { get; init; } = LiveSessionKind.Ambient;
    [DataMember(Order = 19), Key(19)]
    public long VisibleStartLid { get; init; }
    [DataMember(Order = 20), Key(20)]
    public long ContextStartLid { get; init; }
    [DataMember(Order = 21), Key(21)]
    public bool IsExpandedByDefault { get; init; }
    [DataMember(Order = 22), Key(22)]
    public CallOutcome Outcome { get; init; }
    [DataMember(Order = 23), Key(23)]
    public bool HasVideo { get; init; }
    [DataMember(Order = 24), Key(24)]
    public AuthorId? CallerId { get; init; }
    [DataMember(Order = 26), Key(26)]
    public bool HasTranscript { get; init; }

    public static LegacyLiveSessionState? From(LiveSessionState? state)
    {
        if (state is null)
            return null;

        var open = state.OpenConversation;
        return new LegacyLiveSessionState {
            ChatId = state.ChatId,
            StartEntryLid = state.FirstSpeechLid,
            EndEntryLid = open is { HasSummary: true } ? open.SummaryLastLid : state.FirstSpeechLid,
            StartedAt = state.FirstSpeechAt,
            AuthorIds = open?.AuthorIds ?? state.AuthorIds,
            Title = open?.Title ?? "",
            Description = open?.Description ?? "",
            Summary = open?.Summary ?? "",
            TranscriptionOn = state.IsTranscriptionOn,
            MessageCount = open?.SummarizedEntryCount ?? 0,
            LastSummaryAt = open?.SummarizedAt ?? default,
            StartNotificationSent = state.StartNotificationSent,
            Version = state.Version,
            IsClosing = state.IsQuiet,
            ClosingAt = state.QuietSince,
            Host = state.HostAuthorId,
            Rules = state.Rules,
            SessionStartedAt = open?.StartedAt,
            Kind = state.Kind,
            VisibleStartLid = open?.StartLid ?? 0,
            ContextStartLid = open?.ContextStartLid ?? 0,
            IsExpandedByDefault = open?.IsExpandedByDefault ?? false,
            CallerId = state.CallerId,
            HasTranscript = state.HasTranscript,
        };
    }
}
