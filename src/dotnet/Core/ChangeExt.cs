namespace ActualChat;

public static class ChangeExt
{
    public static TChange RequireValid<TChange>(this TChange change)
        where TChange : IChange
    {
        if (!change.IsValid())
            throw StandardError.Constraint("Change must describe a single kind of change (create, update, or remove).");
        return change;
    }
}
