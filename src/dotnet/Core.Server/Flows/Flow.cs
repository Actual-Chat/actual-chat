using ActualChat.Flows.Infrastructure;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

public abstract class Flow<TResult> : Flow
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Result<TResult>? Result {
        get => (Result<TResult>?)UntypedResult;
        protected set => ((IFlowImpl)this).UntypedResult = value;
    }

    public bool IsCompleted(out Result<TResult> result)
    {
        result = Result.GetValueOrDefault();
        return result.HasValue;
    }

    // Protected methods

    protected void Complete(TResult result)
        => Result = new Result<TResult>(result);
    protected void Fail(Exception exception)
        => Result = new Result<TResult>(default!, exception);
}

public abstract class Flow : IFlowImpl
{
    // Properties that are persisted to the DB directly
    [IgnoreDataMember, MemoryPackIgnore]
    public FlowId Id { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public long Version { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public IResult? UntypedResult { get; private set; }

    // IFlowImpl properties
    long IFlowImpl.Version { get => Version; set => Version = value; }
    IResult? IFlowImpl.UntypedResult { get => UntypedResult; set => UntypedResult = value; }

    public override string ToString()
        => $"{GetType().Name}('{Id.Value}', v.{Version.FormatVersion()})";

    public virtual Flow Clone()
        => MemberwiseCloner.Invoke(this);

    // Protected methods

    void IFlowImpl.SetProperties(FlowId id, long version, IResult? untypedResult)
        => SetProperties(id, version, untypedResult);
    protected void SetProperties(FlowId id, long version, IResult? untypedResult)
    {
        Id = id;
        Version = version;
        UntypedResult = untypedResult;
    }

    Task IFlowImpl.Resume(FlowRuntime runtime, CancellationToken cancellationToken)
        => Resume(runtime, cancellationToken);
    protected abstract Task Resume(FlowRuntime runtime, CancellationToken cancellationToken);
}
