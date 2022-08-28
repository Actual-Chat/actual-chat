using ActualChat.Hosting;
using Polly;
using Polly.Extensions.Http;
using Stl.Plugins;

namespace ActualChat.Kubernetes.Module;

public class KubernetesModule : HostModule<KubernetesSettings>
{
    public KubernetesModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public KubernetesModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        services.AddFusion();
        services.AddSingleton<KubeInfo>();
        services.AddSingleton<KubeServices>();
        services.AddHttpClient(Kube.HttpClientName)
            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
            .AddPolicyHandler(GetRetryPolicy());

        static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            var retryDelays = new RetryDelaySeq(0.5, 10);
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(5, retryAttempt => retryDelays[retryAttempt]);
        }
    }
}
