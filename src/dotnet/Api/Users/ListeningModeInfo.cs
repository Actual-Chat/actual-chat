using System.Collections.Frozen;

namespace ActualChat.Users;

public sealed class ListeningModeInfo
{
    public static readonly ListeningModeInfo Default = new(default);

    // Has to go after Default member declaration!
    private static readonly FrozenDictionary<ListeningMode, ListeningModeInfo> Infos
        = Enum.GetValues<ListeningMode>().ToFrozenDictionary(x => x, x => new ListeningModeInfo(x));

    public static readonly ListeningModeInfo[] All = Infos.Values.OrderBy(x => x.Duration).ToArray();

    public ListeningMode Mode { get; }
    public TimeSpan Duration { get; }
    public string Text { get; }
    public bool IsDefault => Mode == default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ListeningModeInfo? Get(ListeningMode mode)
        => Infos.GetValueOrDefault(mode);

    private ListeningModeInfo(ListeningMode mode)
    {
        Mode = mode;
        Duration = GetDuration(mode);
        Text = GetText(Duration);
    }

    private static TimeSpan GetDuration(ListeningMode mode)
    {
        var minutes = (int)mode;
        if (minutes <= 0)
            return Constants.Audio.ListeningDuration;
        if (minutes >= 10_000)
            return TimeSpan.MaxValue;

        return TimeSpan.FromMinutes(minutes);
    }

    private static string GetText(TimeSpan duration)
    {
        if (duration == TimeSpan.MaxValue)
            return "Keep listening";

        var (n, unit) = duration >= TimeSpan.FromHours(1)
            ? ((int)duration.TotalHours, "hour")
            : ((int)duration.TotalMinutes, "minute");
        return $"{n.Format()} {unit.Pluralize(n)}";
    }
}
