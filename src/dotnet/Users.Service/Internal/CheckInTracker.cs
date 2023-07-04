namespace ActualChat.Users.Internal;

internal class CheckInTracker
{
    private readonly ConcurrentDictionary<UserId, CheckIn> _items = new();

    public CheckIn? Get(UserId userId)
        => _items.GetValueOrDefault(userId);

    public void Set(UserId userId, Moment at, bool isActive)
        => _items.AddOrUpdate(
            userId,
            static (_, x) => new (x.at, x.isActive ? x.at : null),
            static (_, prev, x) => x.at >= prev.At ? new (x.at, x.isActive ? x.at : prev.LastActiveAt) : prev,
            (at, isActive));

    public void Remove(UserId userId)
        => _items.TryRemove(userId, out _);
}

public record CheckIn(Moment At, Moment? LastActiveAt)
{
    public CheckIn(Moment at, bool isActive, CheckIn? prev) : this(at, isActive ? at : prev?.LastActiveAt)
    { }
}
