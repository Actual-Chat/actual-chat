namespace ActualChat.Queues;

public interface IComputesTimeout
{
    TimeSpan ComputeTimeout(IServiceProvider services);
}
