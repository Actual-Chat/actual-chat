using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Flows.Db;

[Table("_Flows")]
[Index(nameof(IsCompleted), nameof(Version))]
[Index(nameof(Version), nameof(IsCompleted))]
[Index(nameof(IsFailed), nameof(Version))]
public sealed class DbFlow
{
    [DbKey, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; } = "";
    [ConcurrencyCheck]
    public long Version { get; set; }
    public int DataVersion { get; set; }

    public bool IsCompleted { get; set; }
    public bool IsFailed { get; set; }
    public byte[]? ResultData { get; set; }
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
        DataVersion = flowData.DataVersion;
        ResultData = flowData.ResultData.Length == 0 ? null : flowData.ResultData;
        Data = flowData.Data;
        Console = flowData.Console;
        IsCompleted = flowData.IsCompleted;
        IsFailed = flowData.IsFailed;
    }

    public IFlowData? ToFlowData(Type flowType)
        => ToFlowData(flowType, FlowId.ParseOrNone(Id));
    public IFlowData? ToFlowData(Type flowType, FlowId flowId)
    {
        if (flowId.IsNone || Data == null || Data.Length == 0)
            return null;

        return FlowData.FromData(flowType, flowId, Version, DataVersion, ResultData ?? [], Data, Console);
    }
}
