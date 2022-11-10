namespace ActualChat.Hosting;

public interface IDbInitializer
{
    Dictionary<IDbInitializer, Task> InitializeTasks { get; set; }

    Task Initialize(CancellationToken cancellationToken);
}

public static class DbInitializer
{
    private static readonly AsyncLocal<IDbInitializer?> _current = new();

    public static IDbInitializer? Current {
        get => _current.Value;
        set => _current.Value = value;
    }
}
