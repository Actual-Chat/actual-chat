using ActualChat.Flows.Infrastructure;
using ActualLab.Caching;

namespace ActualChat.Flows;

public interface IFlowData
{
    FlowId Id { get; }
    long Version { get; }
    int DataVersion { get; }
    byte[] ResultData { get; }
    bool IsCompleted { get; }
    bool IsFailed { get; }
    byte[] Data { get; }
    string Console { get; }

    Flow GetFlow(FlowHub hub);
}

public static class FlowData
{
    // Format version 0 = MessagePack (TypeDecorating); legacy fallback = MemoryPack (TypeDecorating)
    // for previously-persisted flow data written before the format byte was introduced.
    public static readonly IByteSerializer FlowSerializer = new VersionedByteSerializer(
        [Serializers.MessagePackTypeDecorating],
        legacy: Serializers.MemoryPackTypeDecorating);
    public static readonly IByteSerializer ResultSerializer = FlowSerializer;

    public static IFlowData FromFlow(Flow flow)
        => GenericInstanceCache
            .GetUnsafe<Func<Flow, IFlowData>>(typeof(FromFlowFactory<>), flow.GetType())
            .Invoke(flow);

    public static IFlowData FromData(
        Type flowType, FlowId id, long version, int dataVersion,
        byte[] resultData, byte[] flowData, string console)
        => GenericInstanceCache
            .GetUnsafe<Func<FlowId, long, int, byte[], byte[], string, IFlowData>>(typeof(FromDataFactory<>), flowType)
            .Invoke(id, version, dataVersion, resultData, flowData, console);

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
            => static (FlowId id, long version, int dataVersion, byte[] resultData, byte[] data, string console)
                => FlowData<TFlow>.FromData(id, version, dataVersion, resultData, data, console);
    }
}

#pragma warning disable CA1000 // Do not declare static members on generic types

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial class FlowData<TFlow> : IFlowData
    where TFlow : Flow
{
    private readonly Lock _lock = new();
    private byte[]? _resultData;
    private byte[]? _data;
    private string? _console;
    private TFlow? _flow;

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public FlowId Id { get; init; }
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public long Version { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public int DataVersion { get; init; }

    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public byte[] ResultData {
        get => _resultData ?? Serialize().ResultData;
        private init {
            _resultData = value;
            _flow = null;
        }
    }

    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)]
    public byte[] Data {
        get => _data ?? Serialize().Data;
        private init {
            _data = value;
            _flow = null;
        }
    }

    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public string Console {
        get => _console ?? Serialize().Console;
        private init {
            _console = value;
            _flow = null;
        }
    }

    // Computed properties

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsCompleted => _flow is { } flow
        ? flow.UntypedResult is not null
        : ResultData.Length != 0;

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsFailed => _flow is { } flow
        ? flow.UntypedResult?.Error is not null
        : _resultData is { Length: > 0 } resultData && ResultHasError(resultData);

    public static FlowData<TFlow> FromFlow(TFlow flow)
        => new FlowData<TFlow>() {
            Id = flow.Id,
            Version = flow.Version,
            DataVersion = flow.DataVersion,
        }.SetFlow(flow);

    public static object FromData(
        FlowId id,
        long version,
        int dataVersion,
        byte[] resultData,
        byte[] data,
        string console)
        => new FlowData<TFlow> {
            Id = id,
            Version = version,
            DataVersion = dataVersion,
            ResultData = resultData,
            Data = data,
            Console = console,
        };

    public Flow GetFlow(FlowHub hub)
    {
        if (_flow is not null) return _flow;
        lock (_lock) { // Double-check locking
            if (_flow is not null) return _flow;

            var expectedDataVersion = hub.Defs.Get<TFlow>().DataVersion;
            if (DataVersion < expectedDataVersion) {
                _flow = DeserializeWithoutData();
                hub.Log.LogInformation(
                    "`{FlowId}`: reset due to data version mismatch ({DataVersion} < {ExpectedDataVersion})",
                    Id, DataVersion, expectedDataVersion);
                return _flow;
            }

            try {
                return _flow = Deserialize();
            }
            catch (Exception e) {
                hub.Log.LogError(e, "`{FlowId}`: reset due to deserialization error", Id);
                return _flow = DeserializeWithoutData();
            }
        }
    }

    // Private methods

    private static bool ResultHasError(byte[] resultData)
    {
        try {
            var result = (IResult?)FlowData.ResultSerializer.Read(resultData, typeof(IResult), out _);
            return result?.Error is not null;
        }
        catch {
            // A completed flow whose result can't be deserialized is treated as failed
            return true;
        }
    }

    private FlowData<TFlow> SetFlow(TFlow flow)
    {
        _flow = flow;

        _resultData = null;
        _data = null;
        _console = null;
        return this;
    }

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

    private TFlow Deserialize()
    {
        // Data, ResultData, Console -> Flow
        var (dataVersion, data, resultData) = (DataVersion, _data.Require(), _resultData.Require());
        var flow = (TFlow)FlowData.FlowSerializer.Read(data, typeof(Flow), out _).Require();
        var untypedResult = resultData is { Length: > 0 }
            ? (IResult?)FlowData.ResultSerializer.Read(resultData, typeof(IResult), out _).Require()
            : null;
        var console = new FlowConsole(flow, _console.Require());
        ((IFlowImpl)flow).SetProperties(Id, Version, dataVersion, untypedResult, console);
        return flow;
    }

    private TFlow DeserializeWithoutData()
    {
        // Data, ResultData, Console -> Flow
        var flow = (TFlow)typeof(TFlow).CreateInstance();
        IResult? untypedResult;
        try {
            var resultData = _resultData.Require();
            untypedResult = resultData is { Length: > 0 }
                ? (IResult?)FlowData.ResultSerializer.Read(resultData, typeof(IResult), out _).Require()
                : null;
        }
        catch (Exception e) {
            untypedResult = Result.NewUntypedError(e);
        }
        var console = new FlowConsole(flow, _console.Require());
        ((IFlowImpl)flow).SetProperties(Id, Version, 0, untypedResult, console);
        return flow;
    }
}
