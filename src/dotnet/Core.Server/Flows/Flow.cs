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

    // Protected methods

    protected void SetResult(TResult result)
        => Result = new Result<TResult>(result);
    protected void SetError(Exception exception)
        => Result = new Result<TResult>(default!, exception);
}

public abstract class Flow : IFlowImpl
{
    // Used during HandleResume & Resume
    [IgnoreDataMember, MemoryPackIgnore]
    protected FlowRuntime Runtime { get; set; } = null!;

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
    {
        var clone = MemberwiseCloner.Invoke(this);
        Runtime = null!;
        return clone;
    }

    // Protected abstract methods

    protected abstract ValueTask Resume(CancellationToken cancellationToken);

    // Protected methods

    void IFlowImpl.SetProperties(FlowId id, long version, IResult? untypedResult)
        => SetProperties(id, version, untypedResult);
    protected void SetProperties(FlowId id, long version, IResult? untypedResult)
    {
        Id = id;
        Version = version;
        UntypedResult = untypedResult;
    }

    Task IFlowImpl.OnResume(IServiceProvider services, CancellationToken cancellationToken)
        => OnResume(services, cancellationToken);
    protected virtual async Task OnResume(IServiceProvider services, CancellationToken cancellationToken)
    {
        Runtime = CreateRuntime(services, cancellationToken);
        try {
            await Resume(cancellationToken).ConfigureAwait(false);
            if (Runtime.AutoCommit)
                await Runtime.Commit(cancellationToken).ConfigureAwait(false);
        }
        finally {
            Runtime = null!;
        }
    }

    protected virtual FlowRuntime CreateRuntime(IServiceProvider services, CancellationToken cancellationToken)
        => new(this, services, cancellationToken);
}
