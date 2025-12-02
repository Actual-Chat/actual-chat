using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Flows.Db;

[Table("_Flows")]
[Index(nameof(Step), nameof(HardResumeAt))]
[Index(nameof(HardResumeAt), nameof(Step))]
[Index(nameof(IsCompleted), nameof(Version))]
[Index(nameof(Version), nameof(IsCompleted))]
public sealed class DbFlow
{
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
    public DbFlow(IFlowData flowData)
        => UpdateFrom(flowData);

    public void UpdateFrom(Flow flow)
        => UpdateFrom(FlowData.FromFlow(flow));
    public void UpdateFrom(IFlowData flowData)
    {
        Id = flowData.Id;
        Version = flowData.Version;
        ResultData = flowData.ResultData.Length == 0 ? null : flowData.ResultData;
        Data = flowData.Data;
        Console = flowData.Console;
        IsCompleted = flowData.IsCompleted;
        Step = flowData.Step;
        HardResumeAt = flowData.HardResumeAt;
    }

    public IFlowData? ToFlowData(Type flowType)
        => ToFlowData(flowType, FlowId.ParseOrNone(Id));
    public IFlowData? ToFlowData(Type flowType, FlowId flowId)
    {
        if (flowId.IsNone || Data == null || Data.Length == 0)
            return null;

        return FlowData.FromData(flowType, flowId, Version, ResultData ?? [], Data, Console, Step, HardResumeAt);
    }
}
