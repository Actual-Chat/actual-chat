namespace ActualChat.UI.Blazor.Services;

public static class ServiceCollectionExt
{
    public static IServiceCollection AddStateRestoreHandler<T>(this IServiceCollection services)
        where T : class, IStateRestoreHandler
        => services.AddScoped<IStateRestoreHandler, T>();
}
