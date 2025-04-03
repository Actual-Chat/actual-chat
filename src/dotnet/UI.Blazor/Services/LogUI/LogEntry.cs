namespace ActualChat.UI.Blazor.Services;

public record LogEntry(
    long Id,
    string CategoryName,
    LogLevel LogLevel,
    EventId EventId,
    string Message,
    Exception? Exception,
    Moment Timestamp) : IVirtualListItem
{
    public string Key { get; } = Id.ToInvariantString();

    public override int GetHashCode()
        => Id.GetHashCode();

    public virtual bool Equals(LogEntry? other)
        => Id == other?.Id;
}
