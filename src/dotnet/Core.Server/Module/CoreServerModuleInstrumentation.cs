
using ActualLab.Diagnostics;

namespace ActualChat.Module;
public static class CoreServerModuleInstrumentation
{
    public static ActivitySource ActivitySource { get; } = typeof(CoreServerModule).GetActivitySource();
}
