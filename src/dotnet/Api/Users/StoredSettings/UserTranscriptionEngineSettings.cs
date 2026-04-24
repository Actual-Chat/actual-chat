using ActualChat.Kvas;
using ActualChat.Transcription;

namespace ActualChat.Users;

/// <summary>
/// User preference for the speech-to-text transcription engine.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record UserTranscriptionEngineSettings
    : StoredSettings, IHasOrigin, IHasKvasKey<UserTranscriptionEngineSettings>
{
    [DataMember, MemoryPackOrder(0), Key(0)] public TranscriptionEngine TranscriptionEngine { get; init; }
        = TranscriptionEngine.Deepgram;
    [DataMember, MemoryPackOrder(1), Key(1)] public string Origin { get; init; } = "";
}
