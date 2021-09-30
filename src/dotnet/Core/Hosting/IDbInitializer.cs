namespace ActualChat.Hosting;

public interface IDbInitializer
{
    bool ShouldRecreateDb { get; set; }
    Dictionary<IDbInitializer, Task> InitializeTasks { get; set; }

    Task Initialize(CancellationToken cancellationToken);
}
