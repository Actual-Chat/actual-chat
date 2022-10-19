namespace ActualChat.Commands;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly record struct QueueRef(
    string Name,
    string? ShardKey = null,
    CommandPriority Priority = CommandPriority.Default)
{
    private static readonly char[] AnyDelimiter = { '.', '[' };

    public static QueueRef Default { get; } = new("default");
    public static QueueRef Chats { get; } = new("chats");
    public static QueueRef Users { get; } = new("users");

    [DataMember(Order = 0)] public string Name { get; init; } = Name;
    [DataMember(Order = 1)] public string ShardKey { get; init; } = ShardKey ?? "";
    [DataMember(Order = 2)] public CommandPriority Priority { get; init; } = Priority;

    public QueueRef(string name, CommandPriority priority)
        : this(name, "", priority) { }

    public override string ToString()
    {
        var prioritySuffix = Priority switch {
            CommandPriority.Critical => "critical",
            CommandPriority.High => "high",
            CommandPriority.Low => "low",
            _ => "",
        };
        return (!ShardKey.IsNullOrEmpty(), Priority is not CommandPriority.Default) switch {
            (false, false) => Name,
            (false, true) => $"{Name}.{prioritySuffix}",
            (true, false) => $"{Name}[{ShardKey}]",
            _ => $"{Name}[{ShardKey}].{prioritySuffix}",
        };
    }

    // Helpers

    public QueueRef ShardBy(string shardKey)
        => this with { ShardKey = shardKey };

    public QueueRef WithPriority(CommandPriority priority)
        => this with { Priority = priority };

    // Parse methods

    public static QueueRef Parse(string value)
        => TryParse(value, out var result) ? result : throw StandardError.Format<QueueRef>();

    public static bool TryParse(string? value, out QueueRef result)
    {
        result = default;
        if (value == null)
            return false;

        var indexOfAnyDelimiter = value.IndexOfAny(AnyDelimiter);
        if (indexOfAnyDelimiter < 0) {
            result = new QueueRef(value);
            return true;
        }

        var name = value[..indexOfAnyDelimiter];
        if (value[indexOfAnyDelimiter] != '[') {
            var prioritySuffix = value[(indexOfAnyDelimiter + 1)..];
            if (GetPriority(prioritySuffix, value) is not { } priority)
                return false;
            result = new QueueRef(name, "", priority);
            return true;
        }

        var shardKeyEndIndex = value.IndexOf(']', indexOfAnyDelimiter);
        if (shardKeyEndIndex < 0 || (value.Length > shardKeyEndIndex + 1 && value[shardKeyEndIndex + 1] != '.'))
            return false;

        var shardKey = value[(indexOfAnyDelimiter + 1)..shardKeyEndIndex];
        var hasPrioritySuffix = value.Length > shardKeyEndIndex + 1 && value[shardKeyEndIndex + 1] == '.';
        if (!hasPrioritySuffix) {
            result = new QueueRef(name, shardKey);
            return true;
        }

        {
            var prioritySuffix = value[(shardKeyEndIndex + 2)..];
            if (GetPriority(prioritySuffix, value) is not { } priority)
                return false;
            result = new QueueRef(name, shardKey, priority);
            return true;
        }
    }

    private static CommandPriority? GetPriority(string prioritySuffix, string? value)
        => prioritySuffix switch {
            "" => default,
            "low" => CommandPriority.Low,
            "high" => CommandPriority.High,
            "critical" => CommandPriority.Critical,
            _ => null,
        };
}
