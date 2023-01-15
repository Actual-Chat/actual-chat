namespace ActualChat.Commands;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly record struct QueueRef(
    [property: DataMember(Order = 0)] Symbol Name,
    [property: DataMember(Order = 1)] Symbol ShardKey = default,
    [property: DataMember(Order = 2)] QueuedCommandPriority Priority = QueuedCommandPriority.Default)
{
    private static readonly char[] AnyDelimiter = { '.', '[' };

    public QueueRef(Symbol name, QueuedCommandPriority priority)
        : this(name, Symbol.Empty, priority) { }

    public override string ToString()
    {
        var prioritySuffix = Priority switch {
            QueuedCommandPriority.Critical => "critical",
            QueuedCommandPriority.High => "high",
            QueuedCommandPriority.Low => "low",
            _ => "",
        };
        return (!ShardKey.IsEmpty, Priority is not QueuedCommandPriority.Default) switch {
            (false, false) => Name,
            (false, true) => $"{Name.Value}.{prioritySuffix}",
            (true, false) => $"{Name.Value}[{ShardKey.Value}]",
            _ => $"{Name.Value}[{ShardKey.Value}].{prioritySuffix}",
        };
    }

    // Helpers

    public QueueRef ShardBy(Symbol shardKey)
        => this with { ShardKey = shardKey };

    public QueueRef WithPriority(QueuedCommandPriority priority)
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

    private static QueuedCommandPriority? GetPriority(string prioritySuffix, string? value)
        => prioritySuffix switch {
            "" => QueuedCommandPriority.Default,
            "low" => QueuedCommandPriority.Low,
            "high" => QueuedCommandPriority.High,
            "critical" => QueuedCommandPriority.Critical,
            _ => null,
        };
}
