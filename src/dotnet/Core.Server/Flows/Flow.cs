using ActualChat.Flows.Infrastructure;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

public abstract class Flow<TResult> : Flow
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Result<TResult>? Result {
        get => (Result<TResult>?)UntypedResult;
        private set => ((IFlowImpl)this).UntypedResult = value;
    }

    // Protected methods

    protected void SetResult(Result<TResult> result, bool mustLog = true)
    {
        Runtime.AutoCommit = true;
        Result = result;
        if (!mustLog)
            return;

        if (result.Error is { } error)
            Console.Log($"[!] {error.GetType().GetName()}, {JsonFormatter.Format(error.Message)}");
        else
            Console.Log($"[=] {result.ValueOrDefault}");
    }

    protected void SetError(Exception error, bool mustLog = true)
        => SetResult(new Result<TResult>(default!, error), mustLog);
}

public abstract class Flow : IFlowImpl
{
    [IgnoreDataMember, MemoryPackIgnore]
    protected FlowRuntime Runtime { get; private set; } = null!;
    [IgnoreDataMember, MemoryPackIgnore]
    protected FlowHub Hub => Runtime?.Hub!;
    [IgnoreDataMember, MemoryPackIgnore]
    protected IServiceProvider Services => Runtime?.Services!;

    // Properties that are persisted to the DB directly
    [IgnoreDataMember, MemoryPackIgnore]
    public FlowId Id { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public long Version { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public int DataVersion { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public IResult? UntypedResult { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public FlowConsole Console { get; private set; } = null!;
    [IgnoreDataMember, MemoryPackIgnore]
    protected Moment ResumedAt { get; private set; }

    // IFlowImpl properties
    long IFlowImpl.Version { get => Version; set => Version = value; }
    int IFlowImpl.DataVersion { get => DataVersion; set => DataVersion = value; }
    IResult? IFlowImpl.UntypedResult { get => UntypedResult; set => UntypedResult = value; }
    FlowConsole IFlowImpl.Console { get => Console; set => Console = value; }
    FlowRuntime? IFlowImpl.Runtime => Runtime;

    public override string ToString()
        => $"{GetType().Name}('{Id.Value}', v.{Version.FormatVersion()})";

    public virtual Flow Clone()
    {
        var clone = MemberwiseCloner.Invoke(this);
        clone.Console = new FlowConsole(clone, Console.ToString()); // No string operations unless Console has non-empty Suffix
        clone.Runtime = null!;
        return clone;
    }

    // Overridable methods

    protected virtual ValueTask Init(CancellationToken cancellationToken) => default;
    protected abstract ValueTask Resume(CancellationToken cancellationToken);

    // Protected methods

    void IFlowImpl.SetProperties(FlowId id, long version, int dataVersion, IResult? untypedResult, FlowConsole flowConsole)
        => SetProperties(id, version, dataVersion, untypedResult, flowConsole);
    protected void SetProperties(FlowId id, long version, int dataVersion, IResult? untypedResult, FlowConsole flowConsole)
    {
        Id = id;
        Version = version;
        DataVersion = dataVersion;
        UntypedResult = untypedResult;
        Console = flowConsole;
    }

    protected virtual FlowRuntime CreateRuntime(FlowHub hub, CancellationToken cancellationToken)
        => new(this, hub, cancellationToken);

    // IFlowImpl methods

    async Task IFlowImpl.HandleResume(FlowHub hub, bool mustReset, CancellationToken cancellationToken)
    {
        var runtime = Runtime = CreateRuntime(hub, cancellationToken);
        ResumedAt = runtime.Hub.Clocks.SystemClock.Now;
        try {
            var flowDef = hub.Defs.Get(GetType());
            var exitReason = (string?)null;
            if (mustReset || DataVersion < flowDef.DataVersion) {
                var initReason = mustReset ? "explicit reset" : "new flow or data version mismatch";
                Console.Log($"[Init] - {initReason}");
                await Init(cancellationToken).ConfigureAwait(false);
                DataVersion = flowDef.DataVersion;
            }
            if (UntypedResult is not null) {
                exitReason = "result is set";
                goto exit;
            }

            Console.Log("[Resume]");
            await Resume(cancellationToken).ConfigureAwait(false);

            exit:
            if (!exitReason.IsNullOrEmpty())
                Console.Log($"[Exit] {exitReason}");
            if (Runtime.AutoCommit)
                await Runtime.Commit(cancellationToken).ConfigureAwait(false);
        }
        finally {
            Runtime = null!;
        }
    }
}
