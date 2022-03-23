using ActualChat.UI.Blazor.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExt
{
    public static IServiceCollection RegisterStateRetainHandler<T>(this IServiceCollection services)
        where T : class, IStateRestoreHandler
        => services.AddScoped<IStateRestoreHandler, T>();
}
