namespace ActualChat.Time;

public enum TimeSpanFormat
{
    Default = 0,
    Short,
    // m:ss, switching to h:mm:ss past an hour — media-player style (e.g. 0:05, not 5s)
    Clock,
}