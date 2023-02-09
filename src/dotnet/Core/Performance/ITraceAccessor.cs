namespace ActualChat.Performance;

public interface ITraceAccessor
{
    public ITraceSession? Trace { get; }
}
