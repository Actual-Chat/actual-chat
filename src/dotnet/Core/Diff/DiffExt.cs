namespace ActualChat.Diff;

public static class DiffExt
{
    public static T Patch<TDiff, T>(this TDiff diff, T source)
        where TDiff : IDiff
        => DiffEngine.Default.Patch(source, diff);

    public static T Patch<TDiff, T>(this TDiff diff, T source, DiffEngine diffEngine)
        where TDiff : IDiff
        => diffEngine.Patch(source, diff);
}
