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
    [IgnoreDataMember, MemoryPackIgnore]
    public FlowConsole Console { get; private set; } = null!;

    // IFlowImpl properties
    long IFlowImpl.Version { get => Version; set => Version = value; }
    IResult? IFlowImpl.UntypedResult { get => UntypedResult; set => UntypedResult = value; }
    FlowConsole IFlowImpl.Console { get => Console; set => Console = value; }

    public override string ToString()
        => $"{GetType().Name}('{Id.Value}', v.{Version.FormatVersion()})";

    public virtual Flow Clone()
    {
        var clone = MemberwiseCloner.Invoke(this);
        clone.Console = new FlowConsole(Console.ToString()); // No string operations unless Console has non-empty Suffix
        clone.Runtime = null!;
        return clone;
    }

    // Protected abstract methods

    protected abstract ValueTask Resume(CancellationToken cancellationToken);

    // Protected methods

    void IFlowImpl.SetProperties(FlowId id, long version, IResult? untypedResult, FlowConsole flowConsole)
        => SetProperties(id, version, untypedResult, flowConsole);
    protected void SetProperties(FlowId id, long version, IResult? untypedResult, FlowConsole flowConsole)
    {
        Id = id;
        Version = version;
        UntypedResult = untypedResult;
        Console = flowConsole;
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
