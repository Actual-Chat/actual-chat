using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Admin-only override pinning transcription to specific transcribers;
/// an empty id means "resolve by ranking".
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record UserTranscriptionEngineSettings
    : StoredSettings, IHasOrigin, IHasKvasKey<UserTranscriptionEngineSettings>
{
    // Key(0) is reserved: it held the TranscriptionEngine enum
    // Key(2) and Key(3) are reserved: they held the separate stream and offline ids, now one pair id
    [DataMember, MemoryPackOrder(1), Key(1)] public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(4), Key(4)] public string Transcriber { get; init; } = "";

    public TranscriberId GetTranscriberId()
        => TranscriberId.TryParse(Transcriber, out var result) ? result : TranscriberId.None;
}
