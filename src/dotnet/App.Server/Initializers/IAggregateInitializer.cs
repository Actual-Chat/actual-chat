namespace ActualChat.App.Server.Initializers;

public interface IAggregateInitializer
{
    Task InvokeAll(CancellationToken cancellationToken);
}
