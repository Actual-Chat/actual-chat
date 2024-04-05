namespace ActualChat.MLSearch.ApiAdapters;

internal static class ServiceProviderExtensions
{
    public static TInstance CreateInstanceWith<TInstance>(this IServiceProvider services, params object[] parameters)
        => ActivatorUtilities.CreateInstance<TInstance>(services, parameters);
}
