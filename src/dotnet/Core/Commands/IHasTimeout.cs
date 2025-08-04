namespace ActualChat;

public interface IHasTimeout
{
    public TimeSpan?  Timeout { get; }
}
