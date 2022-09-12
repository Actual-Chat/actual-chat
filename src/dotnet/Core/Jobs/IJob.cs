namespace ActualChat.Jobs;

public interface IJob : ICommand<Unit>, IBackendCommand
{ }
