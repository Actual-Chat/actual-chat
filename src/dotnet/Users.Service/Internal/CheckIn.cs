namespace ActualChat.Users.Internal;

public record CheckIn(Moment At, Moment? LastActiveAt)
{
    public CheckIn(Moment at, bool isActive, CheckIn? prev) : this(at, isActive ? at : prev?.LastActiveAt)
    { }
}
