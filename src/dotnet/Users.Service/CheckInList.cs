namespace ActualChat.Users;

public record UserCheckIn(UserId UserId, Moment At);

public class CheckIns
{
    private readonly ConcurrentDictionary<UserId, Moment> _items = new ();

    public Moment? Get(UserId userId)
        => _items.GetValueOrDefault(userId);

    public void Set(UserId userId, Moment lastCheckInAt)
    {
        _items.AddOrUpdate(userId, Add, Update);

        Moment Add(UserId _)
            => lastCheckInAt;

        Moment Update(UserId _, Moment current)
            => Moment.Max(lastCheckInAt, current);
    }
}
