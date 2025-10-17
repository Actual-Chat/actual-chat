using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Db;
using ActualChat.Flows.Infrastructure;
using ActualLab.IO;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Flows.Db;

[Table("_Flows")]
[Index(nameof(Step), nameof(HardResumeAt))]
[Index(nameof(HardResumeAt), nameof(Step))]
[Index(nameof(IsCompleted), nameof(Version))]
[Index(nameof(Version), nameof(IsCompleted))]
public sealed class DbFlow : IDbEntity<DbFlow, Flow>
{
    private static readonly IByteSerializer Serializer = TypeDecoratingByteSerializer.Default;

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; } = "";
    [ConcurrencyCheck]
    public long Version { get; set; }

    public bool IsCompleted { get; set; }
    public byte[]? ResultData { get; set; }

    // LegacyFlow properties - to be removed eventually
    [MaxLength(250)]
    public string Step { get; set; } = "";

    public DateTime? HardResumeAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public byte[]? Data { get; set; }
    public string Console { get; set; } = "";

    public DbFlow()
    { }

    public DbFlow(Flow flow)
        => UpdateFrom(flow);

    public void UpdateFrom(Flow flow)
    {
        Id = flow.Id;
        Version = flow.Version;

        using var buffer = new ArrayPoolBuffer<byte>(4096, false);
        Serializer.Write(buffer, flow, typeof(Flow));
        Data = buffer.WrittenSpan.ToArray();
        Console = flow.Console.ToString();

        if (flow.UntypedResult is not null) {
            buffer.Reset();
            Serializer.Write(buffer, flow.UntypedResult, typeof(IResult));
            ResultData = buffer.WrittenSpan.ToArray();
            IsCompleted = true;
        }
        else {
            ResultData = null;
            IsCompleted = false;
        }

        // LegacyFlow properties - to be removed eventually
        var legacyFlow = flow as ILegacyFlowImpl;
        Step = legacyFlow?.Step ?? "";
        HardResumeAt = legacyFlow?.HardResumeAt;
    }

    public Flow? ToModel()
        => ToModel(FlowId.ParseOrNone(Id));
    public Flow? ToModel(FlowId flowId)
    {
        if (flowId.IsNone || Data == null || Data.Length == 0)
            return null;

        var flow = (Flow)Serializer.Read(Data, typeof(Flow), out _).Require();
        var console = new FlowConsole(Console);
        if (flow is ILegacyFlowImpl legacyFlowImpl) // LegacyFlow properties - to be removed eventually
            legacyFlowImpl.SetProperties(flowId, Version, Step, HardResumeAt, console, null);
        else {
            var untypedResult = ResultData is { Length: > 0 }
                ? (IResult?)Serializer.Read(ResultData, typeof(IResult), out _).Require()
                : null;
            ((IFlowImpl)flow).SetProperties(flowId, Version, untypedResult, console);
        }
        return flow;
    }
}
