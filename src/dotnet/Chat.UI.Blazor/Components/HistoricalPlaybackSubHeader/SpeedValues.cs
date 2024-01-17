namespace ActualChat.Chat.UI.Blazor.Components;

public static class SpeedValues
{
    public static readonly SpeedValue Slow = new("0.5", 0.5);
    public static readonly SpeedValue Normal = new("1", 1);
    public static readonly SpeedValue Faster = new("1.33", 1.33);
    public static readonly SpeedValue Fast = new("1.5", 1.5);
    public static readonly SpeedValue Fastest = new("2", 2);

    public static readonly List<SpeedValue> All = [
        Slow,
        Normal,
        Faster,
        Fast,
        Fastest,
    ];
}
