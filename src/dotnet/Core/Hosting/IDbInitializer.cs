namespace ActualChat.Hosting;

public interface IDbInitializer
{
    Dictionary<IDbInitializer, Task> InitializeTasks { get; set; }

    Task Initialize(CancellationToken cancellationToken);
}
