namespace ActualChat;

public static class ChangeExt
{
    public static TChange RequireValid<TChange>(this TChange change)
        where TChange : IChange
    {
        if (!change.IsValid())
            throw InvalidChangeDescriptor();
        return change;
    }

    internal static Exception InvalidChangeDescriptor()
        => new InvalidOperationException("Invalid change descriptor.");
}
