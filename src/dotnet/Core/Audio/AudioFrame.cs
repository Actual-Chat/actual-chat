using ActualChat.Media;

namespace ActualChat.Audio;

[DataContract]
public class AudioFrame : MediaFrame
{
    public override bool IsKeyFrame => true;
}
