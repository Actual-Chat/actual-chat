using ActualChat.Media;

namespace ActualChat.Audio;

[DataContract]
public class AudioStreamPart : IMediaStreamPart<AudioFormat, AudioFrame>
{
    [DataMember(Order = 0)]
    public AudioFormat? Format { get; init; }
    MediaFormat? IMediaStreamPart.Format => Format;

    [DataMember(Order = 1)]
    public AudioFrame? Frame { get; init; }
    MediaFrame? IMediaStreamPart.Frame => Frame;
}
