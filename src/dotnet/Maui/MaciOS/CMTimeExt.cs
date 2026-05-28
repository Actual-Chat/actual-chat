using CoreMedia;

namespace ActualChat.Maui;

public static class CMTimeExt
{
    public static CMTime ToCMTime(this TimeSpan timeSpan, int timescale = 600)
        => CMTime.FromSeconds(timeSpan.TotalSeconds, timescale);
}
