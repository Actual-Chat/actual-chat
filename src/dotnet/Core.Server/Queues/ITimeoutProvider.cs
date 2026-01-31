namespace ActualChat.Queues;

public interface ITimeoutProvider
{
    TimeSpan GetTimeout(IServiceProvider services);
}
