namespace ActualChat.UI.Blazor.Services;

public static class StateFactoryExt
{
    public static async Task<IPersistentState<T>> NewPersistent<T>(
        this IStateFactory factory,
        Func<CancellationToken, Task<T?>> restore,
        Func<T, ISessionCommand> persistCommandFactory,
        CancellationToken cancellationToken = default,
        T initialValue = default!) where T: struct
    {
        var restoredValue = await restore(cancellationToken).ConfigureAwait(false);
        var options = new PersistentState<T>.Options() {
            InitialValue = restoredValue ?? initialValue,
        };
        return new PersistentState<T>(options, persistCommandFactory, factory.Services);
    }

    public static async Task<IMutableState<T>> NewPersistent<T>(
        this IStateFactory factory,
        Func<CancellationToken, Task<T?>> restore,
        Func<T, ISessionCommand> persistCommandFactory,
        CancellationToken cancellationToken = default,
        T initialValue = default!) where T: class
    {
        var restoredValue = await restore(cancellationToken).ConfigureAwait(false);
        var options = new PersistentState<T>.Options() {
            InitialValue = restoredValue ?? initialValue,
        };
        return new PersistentState<T>(options, persistCommandFactory, factory.Services);
    }
}
