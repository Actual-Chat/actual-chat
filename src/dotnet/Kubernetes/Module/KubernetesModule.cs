using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.Extensions.Http;

namespace ActualChat.Kubernetes.Module;

public sealed class KubernetesModule(IServiceProvider moduleServices)
    : HostModule<KubernetesSettings>(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        services.AddFusion();
        services.TryAddSingleton<KubeInfo>();
        services.AddSingleton<IKubeInfo>(c => c.GetRequiredService<KubeInfo>());
        services.AddSingleton<KubeServices>();
        services.AddSingleton<KubeLeaseClient>();
        services.AddSingleton<KubeMeshLocks>();
        services.AddHttpClient(Kube.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(c => {
                var handler = new SocketsHttpHandler {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    MaxConnectionsPerServer = 20,
                    EnableMultipleHttp2Connections = true,
                    KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
                    KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always
                };
                var kubeInfo = c.GetRequiredService<KubeInfo>();
                var log = c.LogFor<KubeServices>();
                var caCertString = File.ReadAllText(kubeInfo.CACertPath);
                var caCert = X509Certificate2.CreateFromPem(caCertString);
#pragma warning disable MA0039
                handler.SslOptions.RemoteCertificateValidationCallback =
                        (_, cert, _, policyErrors) =>
                        {
                            if (cert is not X509Certificate2 x509Cert)
                                return false;
                            if (policyErrors != SslPolicyErrors.RemoteCertificateChainErrors)
                                return false;

                            try {
                                using var x509Chain = new X509Chain();
                                x509Chain.ChainPolicy.ExtraStore.Add(caCert);
                                x509Chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                                x509Chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                                return x509Chain.Build(x509Cert);
                            }
                            catch (Exception ex)
                            {
                                log.LogError(ex, "Error validation certificate chain during Kubernetes API call");
                                return false;
                            }
                        };
                return handler;
#pragma warning restore MA0039
            })
            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
            .AddPolicyHandler(GetRetryPolicy());

        static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            var retryDelays = RetryDelaySeq.Exp(0.5, 10);
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(5, retryAttempt => retryDelays[retryAttempt]);
        }
    }
}
