namespace ActualChat.Live;

/// <summary>
/// Audio data packet in a multiplexed live stream.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial class MuxedAudioFrame : MuxedAudioStreamItem
{
    [DataMember(Order = 1), Key(3)]
    public ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>
    /// Frame offset from start of the audio stream (populated from AudioFrame.Offset).
    /// Used by per-author merge to detect overlapping streams.
    /// </summary>
    [DataMember(Order = 2), Key(4)]
    public TimeSpan Offset { get; init; }
}
