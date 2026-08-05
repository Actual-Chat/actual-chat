namespace ActualChat.Transcription;

// An empty Languages / DetectLanguages set means "no declared restriction", not "nothing supported" —
// several providers cover far more languages than Languages.All enumerates.

/// <summary>
/// Declares what a single transcriber configuration can do, so that provider selection
/// can filter and rank candidates without calling them.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record TranscriberInfo
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public TranscriberId Id { get; init; } = TranscriberId.None;
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public Symbol DriverId { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public TranscriberKind Kind { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public ApiSet<Language> Languages { get; init; } = new();
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)]
    public ApiSet<Language> DetectLanguages { get; init; } = new();
    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public bool IsLanguageDetectionSupported { get; init; }
    [DataMember(Order = 6), MemoryPackOrder(6), Key(6)]
    public TranscriptionContextPolicy? ContextPolicy { get; init; }
    [DataMember(Order = 7), MemoryPackOrder(7), Key(7)]
    public TranscriberInfo? Retranscriber { get; init; }
    [DataMember(Order = 8), MemoryPackOrder(8), Key(8)]
    public TimeSpan MinRetranscriptionDuration { get; init; } = Constants.Transcription.MinRetranscriptionDuration;
    [DataMember(Order = 9), MemoryPackOrder(9), Key(9)]
    public int MinRetranscriptionWords { get; init; } = Constants.Transcription.MinRetranscriptionWords;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsStream => Kind is TranscriberKind.StreamOnly or TranscriberKind.StreamSelfRefined;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsOffline => Kind == TranscriberKind.OfflineOnly;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsContextSupported => ContextPolicy != null || Retranscriber?.ContextPolicy != null;

    public bool IsSupported(Language language)
        => Languages.Count == 0 || Languages.Contains(language);

    public bool IsDetectionSupported(IReadOnlyCollection<Language> candidates)
    {
        if (!IsLanguageDetectionSupported)
            return false;
        if (DetectLanguages.Count == 0)
            return true;

        foreach (var candidate in candidates)
            if (!DetectLanguages.Contains(candidate))
                return false;

        return true;
    }
}
