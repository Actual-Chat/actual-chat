using ActualChat.Flows.Infrastructure;
using ActualLab.Versioning;

namespace ActualChat.Flows;

public abstract class Flow<TResult> : Flow
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
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
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    protected FlowRuntime Runtime { get; private set; } = null!;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    protected FlowHub Hub => Runtime?.Hub!;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    protected IServiceProvider Services => Runtime?.Services!;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    protected FlowDef FlowDef => Runtime.FlowDef;

    // Properties that are persisted to the DB directly
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public FlowId Id { get; private set; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public long Version { get; private set; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int DataVersion { get; private set; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public IResult? UntypedResult { get; private set; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public FlowConsole Console { get; private set; } = null!;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
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

    async Task IFlowImpl.HandleResume(FlowHub hub, string? initReason, CancellationToken cancellationToken)
    {
        var runtime = Runtime = CreateRuntime(hub, cancellationToken);
        ResumedAt = runtime.Hub.Clocks.SystemClock.Now;
        try {
            var exitReason = (string?)null;
            if (initReason is not null) {
                Console.Log($"[Init] - {initReason}");
                await Init(cancellationToken).ConfigureAwait(false);
            }
            if (UntypedResult is not null) {
                exitReason = "result is set";
                goto exit;
            }

            Console.Log($"[Resume] at {ResumedAt.ToDateTime():yy-MM-dd HH:mm:ss.fff}");
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
