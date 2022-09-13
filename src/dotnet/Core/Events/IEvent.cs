namespace ActualChat.Events;

public interface IEvent : ICommand<Unit>, IBackendCommand
{ }
