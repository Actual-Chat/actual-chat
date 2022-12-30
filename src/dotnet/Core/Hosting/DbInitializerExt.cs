namespace ActualChat.Hosting;

public static class DbInitializerExt
{
    public static TDbInitializer GetOtherInitializer<TDbInitializer>(this IDbInitializer initializer)
        where TDbInitializer : IDbInitializer
    {
        foreach (var (i, _) in initializer.InitializeTasks) {
            if (i is TDbInitializer result)
                return result;
        }
        throw StandardError.NotFound<TDbInitializer>();
    }
}
