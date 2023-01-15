namespace ActualChat.Hosting;

public interface IDbInitializer : IHasServices
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

    public static TDbInitializer Get<TDbInitializer>()
        where TDbInitializer : IDbInitializer
    {
        var current = Current ?? throw StandardError.Internal("DbInitializer.Current == null");
        if (current is TDbInitializer result)
            return result;

        return current.GetOtherInitializer<TDbInitializer>();
    }
}
