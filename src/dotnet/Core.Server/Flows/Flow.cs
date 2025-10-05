using ActualLab.Diagnostics;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

public abstract class Flow : IHasId<FlowId>
{
    protected static readonly bool DebugMode = Constants.DebugMode.Flows;

    protected IServiceProvider? Services { get; set; }
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

    public void Initialize(FlowId id, long version, IServiceProvider? services = null)
    {
        Id = id;
        Version = version;
        Services = services;
    }

    public override string ToString()
        => $"{GetType().Name}('{Id.Value}', v.{Version.FormatVersion()})";

    public virtual Flow Clone()
        => MemberwiseCloner.Invoke(this);
}
