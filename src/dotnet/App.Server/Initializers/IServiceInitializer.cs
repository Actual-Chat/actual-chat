namespace ActualChat.App.Server.Initializers;

public interface IServiceInitializer
{
    Task Invoke(CancellationToken cancellationToken);
}
