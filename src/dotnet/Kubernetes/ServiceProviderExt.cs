namespace ActualChat.Kubernetes;

public static class ServiceProviderExt
{
    public static IKubeInfo KubeInfo(this IServiceProvider services)
        => services.GetRequiredService<KubeInfo>();

    public static KubeServices KubeServices(this IServiceProvider services)
        => services.GetRequiredService<KubeServices>();
}
