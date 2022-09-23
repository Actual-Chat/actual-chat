namespace ActualChat.Commands;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct QueueRef
{
    [DataMember(Order = 0)]
    public string Ref { get; }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public QueueRef(string @ref)
        => Ref = @ref;

    public QueueRef(string queueName, string? shardKey = null, CommandPriority priority = CommandPriority.Default)
    {
        var prioritySuffix = priority switch {
            CommandPriority.Critical => "critical",
            CommandPriority.High => "high",
            CommandPriority.Low => "low",
            _ => "",
        };
        var hasNoShardKey = string.IsNullOrWhiteSpace(shardKey);
        var hasNoPriority = string.IsNullOrWhiteSpace(prioritySuffix);
        Ref = (hasNoShardKey, hasNoPriority) switch {
            (true, true) => queueName,
            (true, false) => $"{queueName}.{prioritySuffix}",
            (false, true) => $"{queueName}[{shardKey}]",
            _ => $"{queueName}[{shardKey}].{prioritySuffix}",
        };
    }
}
