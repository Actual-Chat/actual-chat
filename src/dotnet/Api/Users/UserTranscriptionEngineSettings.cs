using ActualChat.Kvas;
using ActualChat.Transcription;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserTranscriptionEngineSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserTranscriptionEngineSettings);

    [DataMember, MemoryPackOrder(0)] public TranscriptionEngine TranscriptionEngine{ get; init; } = TranscriptionEngine.Google;
    [DataMember, MemoryPackOrder(1)] public string Origin { get; init; } = "";
}
