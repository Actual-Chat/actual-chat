using ActualChat.Flows.Infrastructure;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

public abstract class Flow : IFlowImpl
{
    // Persisted to the DB directly
    [IgnoreDataMember, MemoryPackIgnore]
    public FlowId Id { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public long Version { get; internal set; }

    public override string ToString()
        => $"{GetType().Name}('{Id.Value}', v.{Version.FormatVersion()})";

    public virtual Flow Clone()
        => MemberwiseCloner.Invoke(this);

    void IFlowImpl.Initialize(FlowId id, long version)
        => Initialize(id, version);
    protected void Initialize(FlowId id, long version)
    {
        Id = id;
        Version = version;
    }

    Task IFlowImpl.Resume(FlowRuntime runtime, CancellationToken cancellationToken)
        => Resume(runtime, cancellationToken);
    protected abstract Task Resume(FlowRuntime runtime, CancellationToken cancellationToken);
}
