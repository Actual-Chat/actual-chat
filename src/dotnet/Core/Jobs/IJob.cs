namespace ActualChat.Jobs;

public interface IJob<out T> : ICommand<Unit>, IBackendCommand
{
    T Data { get; }
}
