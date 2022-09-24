namespace ActualChat.Commands;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct QueueRef
{
    public static readonly QueueRef Default = new ("default");

    [DataMember(Order = 0)]
    public string Ref { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string QueueName { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string? ShardKey { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public CommandPriority Priority { get; }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public QueueRef(string @ref)
    {
        Ref = @ref ?? throw new ArgumentNullException(nameof(@ref));
        var indexOfAnyBoundary = @ref.IndexOfAny(new[] { '.', '[' });
        if (indexOfAnyBoundary < 0) {
            QueueName = @ref;
            ShardKey = null;
            Priority = CommandPriority.Default;
        }
        else if (@ref[indexOfAnyBoundary] == '[') {
            QueueName = @ref[..indexOfAnyBoundary];
            var shardKeyEndIndex = @ref.IndexOf(']', indexOfAnyBoundary);
            if (shardKeyEndIndex < 0 || (@ref.Length > shardKeyEndIndex + 1 && @ref[shardKeyEndIndex + 1] != '.'))
                throw StandardError.Constraint<QueueRef>(@ref);

            ShardKey = @ref[(indexOfAnyBoundary + 1)..shardKeyEndIndex];
            var hasPrioritySuffix = @ref.Length > shardKeyEndIndex + 1 && @ref[shardKeyEndIndex + 1] == '.';
            if (hasPrioritySuffix) {
                var prioritySuffix = @ref[(shardKeyEndIndex + 2)..];
                Priority = GetPriority(prioritySuffix);
            }
            else
                Priority = CommandPriority.Default;
        }
        else {
            QueueName = @ref[..indexOfAnyBoundary];
            ShardKey = null;
            var prioritySuffix = @ref[(indexOfAnyBoundary + 1)..];
            Priority = GetPriority(prioritySuffix);
        }

        CommandPriority GetPriority(string prioritySuffix)
        {
            return prioritySuffix switch {
                "critical" => CommandPriority.Critical,
                "high" => CommandPriority.High,
                "low" => CommandPriority.Low,
                _ => throw StandardError.Constraint<QueueRef>(@ref),
            };
        }
    }

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
        QueueName = queueName;
        ShardKey = shardKey;
        Priority = priority;
    }
}
