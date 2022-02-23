using ActualChat.Media;

namespace ActualChat.Audio;

[DataContract]
public class AudioFrame : MediaFrame
{
    [DataMember(Order = 1)]
    public override TimeSpan Offset { get; init; }
    public override TimeSpan Duration => TimeSpan.FromMilliseconds(20);
    public override bool IsKeyFrame => true;
}
