using ActualChat.Audio;
using ActualChat.Rtc;

namespace ActualChat.UI.Blazor.App.Services.Rtc;

/// <summary>
/// Arguments for the StreamStarted event.
/// </summary>
public sealed record RtcStreamStartedArgs(
    RtcStreamStart StreamInfo,
    IAsyncEnumerable<byte[]> AudioFrames)
{
    public int StreamIndex => StreamInfo.StreamIndex;
    public Moment BeginsAt => StreamInfo.BeginsAt;
    public AuthorId? AuthorId => StreamInfo.AuthorId;
    public ChatEntryId? EntryId => StreamInfo.EntryId;
    public AudioFormat? Format => StreamInfo.Format;
}
