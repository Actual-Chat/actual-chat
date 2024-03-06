namespace ActualChat.Hosting;

public interface IModuleInitializer
{
    Task Initialize(CancellationToken cancellationToken);
}
