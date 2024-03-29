using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using ActualChat.Hosting;
using Polly;
using Polly.Extensions.Http;

namespace ActualChat.Kubernetes.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class KubernetesModule(IServiceProvider moduleServices)
    : HostModule<KubernetesSettings>(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        services.AddFusion();
        services.AddSingleton<KubeInfo>();
        services.AddSingleton<KubeServices>();
        services.AddHttpClient(Kube.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(c => {
                var handler = new HttpClientHandler();
                var kubeInfo = c.GetRequiredService<KubeInfo>();
                var log = c.LogFor<KubeServices>();
                var caCertString = File.ReadAllText(kubeInfo.CACertPath);
                var caCert = X509Certificate2.CreateFromPem(caCertString);
#pragma warning disable MA0039
                handler.ServerCertificateCustomValidationCallback =
                    handler.ServerCertificateCustomValidationCallback =
                        (_, cert, _, policyErrors) =>
                        {
                            if (cert == null)
                                return false;
                            if (policyErrors != SslPolicyErrors.RemoteCertificateChainErrors)
                                return false;

                            try {
                                using var x509Chain = new X509Chain();
                                x509Chain.ChainPolicy.ExtraStore.Add(caCert);
                                x509Chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                                x509Chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                                return x509Chain.Build(cert);
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
