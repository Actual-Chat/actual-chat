using ActualLab.Diagnostics;

namespace ActualChat.Db.Module;

public static class DbModuleInstrumentation
{
    public static ActivitySource ActivitySource { get; } = typeof(DbModule).GetActivitySource();
}
