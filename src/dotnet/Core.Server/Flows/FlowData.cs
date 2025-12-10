using ActualChat.Flows.Infrastructure;
using ActualLab.Caching;
using ActualLab.IO;
using MemoryPack;

namespace ActualChat.Flows;

public interface IFlowData
{
    FlowId Id { get; }
    long Version { get; }
    byte[] ResultData { get; }
    bool IsCompleted { get; }
    byte[] Data { get; }
    Flow Flow { get; }
    string Console { get; }
    string Step { get; }
    Moment? HardResumeAt { get; }
}

public static class FlowData
{
    public static readonly IByteSerializer FlowSerializer = TypeDecoratingByteSerializer.Default;
    public static readonly IByteSerializer ResultSerializer = TypeDecoratingByteSerializer.Default;

    public static IFlowData FromFlow(Flow flow)
        => GenericInstanceCache
            .GetUnsafe<Func<Flow, IFlowData>>(typeof(FromFlowFactory<>), flow.GetType())
            .Invoke(flow);

    public static IFlowData FromData(
        Type flowType, FlowId id, long version,
        byte[] resultData, byte[] flowData, string console,
        string step, Moment? hardResumeAt)
        => GenericInstanceCache
            .GetUnsafe<Func<FlowId, long, byte[], byte[], string, string, Moment?, IFlowData>>(typeof(FromDataFactory<>), flowType)
            .Invoke(id, version, resultData, flowData, console, step, hardResumeAt);

    // Nested types

    private sealed class FromFlowFactory<TFlow> : GenericInstanceFactory, IGenericInstanceFactory<TFlow>
        where TFlow : Flow
    {
        public override object Generate()
            => static (Flow flow) => FlowData<TFlow>.FromFlow((TFlow)flow);
    }

    private sealed class FromDataFactory<TFlow> : GenericInstanceFactory, IGenericInstanceFactory<TFlow>
        where TFlow : Flow
    {
        public override object Generate()
            => static (FlowId id, long version, byte[] resultData, byte[] data, string console, string step, Moment? hardResumeAt)
                => FlowData<TFlow>.FromData(id, version, resultData, data, console, step, hardResumeAt);
    }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class FlowData<TFlow> : IFlowData
    where TFlow : Flow
{
    private byte[]? _resultData;
    private byte[]? _data;
    private string? _console;
    private TFlow? _flow;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public FlowId Id { get; init; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public long Version { get; init; }

    [DataMember(Order = 2), MemoryPackOrder(2)]
    public byte[] ResultData {
        get => _resultData ?? Serialize().ResultData;
        private init {
            _resultData = value;
            _flow = null;
        }
    }

    [DataMember(Order = 3), MemoryPackOrder(3)]
    public byte[] Data {
        get => _data ?? Serialize().Data;
        private init {
            _data = value;
            _flow = null;
        }
    }

    [DataMember(Order = 4), MemoryPackOrder(4)]
    public string Console {
        get => _console ?? Serialize().Console;
        private init {
            _console = value;
            _flow = null;
        }
    }

    [DataMember(Order = 5), MemoryPackOrder(5)]
    public string Step { get; init; } = "";
    [DataMember(Order = 6), MemoryPackOrder(6)]
    public Moment? HardResumeAt { get; init; }

    // Computed properties

    [IgnoreDataMember, MemoryPackIgnore]
    Flow IFlowData.Flow => Flow;

    [IgnoreDataMember, MemoryPackIgnore]
    public bool IsCompleted => _flow is { } flow
        ? flow.UntypedResult is not null
        : ResultData.Length != 0;

    [IgnoreDataMember, MemoryPackIgnore]
    public TFlow Flow {
        get => _flow ?? Deserialize().Flow;
        private init {
            _flow = value;
            _resultData = null;
            _data = null;
            _console = null;
        }
    }

    public static FlowData<TFlow> FromFlow(TFlow flow)
    {
        var legacyFlowImpl = flow as ILegacyFlowImpl;
        return new FlowData<TFlow> {
            Id = flow.Id,
            Version = flow.Version,
            Step = legacyFlowImpl?.Step.Value ?? "",
            HardResumeAt = legacyFlowImpl?.HardResumeAt,
            Flow = flow, // Flow must be set at the very end
        };
    }

    public static object FromData(
        FlowId id,
        long version,
        byte[] resultData,
        byte[] data,
        string console,
        string step,
        Moment? hardResumeAt)
        => new FlowData<TFlow> {
            Id = id,
            Version = version,
            ResultData = resultData,
            Data = data,
            Console = console,
            Step = step,
            HardResumeAt = hardResumeAt,
        };

    // Private methods

    private FlowData<TFlow> Serialize()
    {
        // Flow -> Data, ResultData, Console
        var flow = _flow.Require();
        using var buffer = new ArrayPoolBuffer<byte>(4096, false);
        if (flow.UntypedResult is null)
            _resultData = [];
        else {
            FlowData.ResultSerializer.Write(buffer, flow.UntypedResult, typeof(IResult));
            _resultData = buffer.WrittenSpan.ToArray();
            buffer.Reset();
        }
        FlowData.FlowSerializer.Write(buffer, flow, typeof(Flow));
        _data = buffer.WrittenSpan.ToArray();
        _console = flow.Console.ToString();
        return this;
    }

    private FlowData<TFlow> Deserialize()
    {
        // Data, ResultData, Console -> Flow
        var (data, resultData, console) = (_data.Require(), _resultData.Require(), new FlowConsole(_console.Require()));
        using var buffer = new ArrayPoolBuffer<byte>(4096, false);
        var flow = (TFlow)FlowData.FlowSerializer.Read(data, typeof(Flow), out _).Require();
        if (flow is ILegacyFlowImpl legacyFlowImpl) // LegacyFlow properties - to be removed eventually
            legacyFlowImpl.SetProperties(Id, Version, Step, HardResumeAt, console, null);
        else {
            var untypedResult = resultData is { Length: > 0 }
                ? (IResult?)FlowData.ResultSerializer.Read(resultData, typeof(IResult), out _).Require()
                : null;
            ((IFlowImpl)flow).SetProperties(Id, Version, untypedResult, console);
        }
        _flow = flow;
        return this;
    }
}
