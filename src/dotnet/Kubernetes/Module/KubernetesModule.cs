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

        var fusion = services.AddFusion();
        services.AddHttpClient<ServiceRegistry>()
            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
            .AddPolicyHandler(GetRetryPolicy());

        static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(
                    5,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }
    }
}

