namespace ActualChat.Hosting;

/// <summary>
/// Interface for database schema and data initialization.
/// </summary>
public interface IDbInitializer : IHasServices
{
#pragma warning disable CA2227
    Dictionary<IDbInitializer, Task> RunningTasks { get; set; }
#pragma warning restore CA2227
    bool ShouldRepairData { get; }
    bool ShouldVerifyData { get; }

    Task InitializeSchema(CancellationToken cancellationToken);
    Task InitializeData(CancellationToken cancellationToken);
    Task RepairData(CancellationToken cancellationToken);
    Task VerifyData(CancellationToken cancellationToken);
}

/// <summary>
/// Helper methods for accessing the current <see cref="IDbInitializer"/> instance.
/// </summary>
public static class DbInitializer
{
    private static readonly AsyncLocal<IDbInitializer?> _current = new();

    public static ClosedDisposable<IDbInitializer?> Activate(this IDbInitializer dbInitializer)
    {
        var oldCurrent = _current.Value;
        if (oldCurrent == dbInitializer)
            return default;

        _current.Value = dbInitializer;
        return Disposable.NewClosed(oldCurrent, oldCurrent1 => _current.Value = oldCurrent1);
    }

    public static TDbInitializer GetCurrent<TDbInitializer>()
        where TDbInitializer : IDbInitializer
    {
        var current = _current.Value ?? throw StandardError.Internal("DbInitializer.Current == null");
        return (TDbInitializer)current;
    }

    public static TDbInitializer GetOther<TDbInitializer>()
        where TDbInitializer : IDbInitializer
    {
        var current = GetCurrent<IDbInitializer>();
        if (current is TDbInitializer result)
            return result;

        return current.GetOtherInitializer<TDbInitializer>();
    }
}
