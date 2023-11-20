using ActualChat.Media;
using MemoryPack;

namespace ActualChat.Audio;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class AudioFrame : MediaFrame
{
    [DataMember(Order = 4), MemoryPackOrder(4)]
    public override TimeSpan Offset { get; init; }

    public override TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(20);
    public override bool IsKeyFrame => true;
}
