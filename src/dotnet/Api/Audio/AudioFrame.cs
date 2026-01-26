using ActualChat.Media;

namespace ActualChat.Audio;

/// <summary>
/// Represents a single frame of audio data.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class AudioFrame : MediaFrame
{
    [DataMember(Order = 4), MemoryPackOrder(4)]
    public override TimeSpan Offset { get; init; }

    public override TimeSpan Duration { get; init; } = Constants.Audio.OpusFrameDuration;
    public override bool IsKeyFrame { get; init; } = true;
}
