namespace ActualChat.Diff;

public static class DiffExt
{
    public static T Patch<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDiff,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(this TDiff diff, T source)
        where TDiff : IDiff
        => DiffEngine.Default.Patch(source, diff);

    public static T Patch<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDiff,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        this TDiff diff,
        T source,
        DiffEngine diffEngine)
        where TDiff : IDiff
        => diffEngine.Patch(source, diff);
}
