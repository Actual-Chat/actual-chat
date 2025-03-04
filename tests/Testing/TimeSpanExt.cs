using System.Diagnostics;

namespace ActualChat.Testing;

public static class TimeSpanExt
{
    public static TimeSpan Debuggable(this TimeSpan timeSpan)
        => !Debugger.IsAttached ? timeSpan : timeSpan + TimeSpan.FromMinutes(15);
}
