namespace ActualChat.Hosting;

/// <summary>
/// Performs async initialization after all modules are registered.
/// </summary>
public interface IModuleInitializer
{
    Task Initialize(CancellationToken cancellationToken);
}
