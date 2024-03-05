namespace ActualChat;

public static class TaskFactoryExt
{
    public static Func<CancellationToken, Task<Unit>> ToUnitTaskFactory(this Func<CancellationToken, Task> taskFactory)
        => async ct => {
            await taskFactory.Invoke(ct).ConfigureAwait(false);
            return Unit.Default;
        };
}
