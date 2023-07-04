namespace ActualChat.Hosting;

public static class DbInitializerExt
{
    public static TDbInitializer GetOtherInitializer<TDbInitializer>(this IDbInitializer dbInitializer)
        where TDbInitializer : IDbInitializer
    {
        foreach (var (x, _) in dbInitializer.RunningTasks) {
            if (x == dbInitializer)
                continue;
            if (x is TDbInitializer result)
                return result;
        }
        throw StandardError.NotFound<TDbInitializer>();
    }

    public static async Task WaitForOtherInitializers(
        this IDbInitializer dbInitializer,
        Func<IDbInitializer, bool> isWaitTargetPredicate)
    {
        foreach (var (x, task) in dbInitializer.RunningTasks) {
            if (x == dbInitializer)
                continue;
            if (isWaitTargetPredicate.Invoke(x))
                await task.ConfigureAwait(false);
        }
    }
}
