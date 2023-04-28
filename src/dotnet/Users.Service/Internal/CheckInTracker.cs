namespace ActualChat.Users.Internal;

internal class CheckInTracker
{
    private readonly ConcurrentDictionary<UserId, Moment> _items = new();

    public Moment? Get(UserId userId)
        => _items.GetValueOrDefault(userId);

    public void Set(UserId userId, Moment lastCheckInAt)
        => _items.AddOrUpdate(
            userId,
            static (_, lastCheckInAt1) => lastCheckInAt1,
            static (_, prevCheckInAt, lastCheckInAt1) => Moment.Max(prevCheckInAt, lastCheckInAt1),
            lastCheckInAt);

    public void Remove(UserId userId)
        => _items.TryRemove(userId, out _);
}
