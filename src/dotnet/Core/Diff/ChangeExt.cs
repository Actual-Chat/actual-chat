using System.ComponentModel.DataAnnotations;

namespace ActualChat.Diff;

public static class ChangeExt
{
    public static TChange RequireValid<TChange>(this TChange change)
        where TChange : IChange
    {
        if (!change.IsValid())
            throw new ValidationException("Invalid change descriptor.");
        return change;
    }
}
