using ActualChat.Flows.Infrastructure;
using ActualLab.Diagnostics;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

public abstract class Flow : IFlowImpl
{
    protected static readonly bool DebugMode = Constants.DebugMode.Flows;

    IServiceProvider? IFlowImpl.Services { get => Services; set => Services = value; }
    protected IServiceProvider? Services { get; private set; }
    [field: AllowNull, MaybeNull]
    protected MomentClockSet Clocks => field ??= Services.Require().Clocks();

    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.Require().LogFor(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, DebugMode);

    // Persisted to the DB directly
    [IgnoreDataMember, MemoryPackIgnore]
    public FlowId Id { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public long Version { get; internal set; }

    public override string ToString()
        => $"{GetType().Name}('{Id.Value}', v.{Version.FormatVersion()})";

    public virtual Flow Clone()
        => MemberwiseCloner.Invoke(this);

    void IFlowImpl.Initialize(FlowId id, long version, IServiceProvider? services)
        => Initialize(id, version, services);
    protected void Initialize(FlowId id, long version, IServiceProvider? services)
    {
        Id = id;
        Version = version;
        Services = services;
    }

    Task IFlowImpl.Resume(CancellationToken cancellationToken)
        => Resume(cancellationToken);
    protected abstract Task Resume(CancellationToken cancellationToken);
}
