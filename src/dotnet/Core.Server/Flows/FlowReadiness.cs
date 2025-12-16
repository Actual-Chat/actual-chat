using MemoryPack;
using MessagePack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: MemoryPackConstructor, SerializationConstructor, JsonConstructor, Newtonsoft.Json.JsonConstructor]
public readonly partial record struct FlowReadiness(string SuspensionReason, TimeSpan? ResumeDelay = null)
{
    public static FlowReadiness Ready => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public string SuspensionReason { get => field ?? ""; init; } = SuspensionReason;
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public TimeSpan? ResumeDelay { get => IsSuspended ? field : TimeSpan.Zero; init; } = ResumeDelay;

    // Computed
    [IgnoreDataMember, MemoryPackIgnore, JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsSuspended => !SuspensionReason.IsNullOrEmpty();

    public override string ToString()
    {
        var resumeDelay = ResumeDelay is { } v ? v.ToShortString() : "null";
        return IsSuspended
            ? $"{nameof(FlowReadiness)}({JsonFormatter.Format(SuspensionReason)}, {nameof(ResumeDelay)}={resumeDelay})"
            : $"{nameof(FlowReadiness)}.{nameof(Ready)}";
    }

    public static implicit operator FlowReadiness(string suspensionReason)
        => new (suspensionReason);
    public static implicit operator FlowReadiness((string SuspensionReason, TimeSpan ResumeDelay) pair)
        => new (pair.SuspensionReason) { ResumeDelay = pair.ResumeDelay };
}
