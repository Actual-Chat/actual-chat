namespace ActualChat.App.Server;

internal class RootTraceAccessor : ITraceAccessor
{
    public static readonly RootTraceAccessor Instance = new RootTraceAccessor();

    private RootTraceAccessor() { }

    public ITraceSession? Trace { get; set; }
}
