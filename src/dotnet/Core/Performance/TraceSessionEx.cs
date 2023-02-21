namespace ActualChat.Performance;

public static class TraceSessionEx
{
    public static bool IsEnabled(this ITraceSession trace)
        => trace != TraceSession.Null;
}
