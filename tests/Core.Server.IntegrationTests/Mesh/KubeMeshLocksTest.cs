using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using ActualChat.Kubernetes;
using Polly;
using Polly.Extensions.Http;

namespace ActualChat.Core.Server.IntegrationTests.Mesh;

public class KubeMeshLocksTest(ITestOutputHelper @out) : TestBase(@out)
{
    private IServiceProvider Services { get; set; } = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddFusion();
        serviceCollection.AddSingleton(KubeInfo.GetLocal)
            .AddSingleton<IKubeInfo>(c => c.GetRequiredService<KubeInfo>())
            .AddSingleton<KubeLeaseClient>()
            .AddSingleton<KubeMeshLocks>()
            .AddSingleton<ILoggerFactory>(c => new LoggerFactory().AddXUnit(Out))
            .AddHttpClient(Kube.HttpClientName)
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
        Services = serviceCollection.BuildServiceProvider();
        return;

        static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            var retryDelays = RetryDelaySeq.Exp(0.5, 10);
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(5, retryAttempt => retryDelays[retryAttempt]);
        }
    }

    protected override async Task DisposeAsync()
        => await base.DisposeAsync();

    [Fact(Skip = "For manual testing only with local docker-desktop k8s")]
    public async Task KubeMeshLocks_Basic_Works()
    {
        var locks = Services.GetRequiredService<KubeMeshLocks>();
        var key = "test-lock-" + Guid.NewGuid().ToString("N")[..8];

        // 1. TryLock
        var holder = await locks.TryLock(key);
        holder.Should().NotBeNull();
        try {
            holder!.Key.Should().Be(key);

            // 2. TryLock again (should fail)
            var holder2 = await locks.TryLock(key);
            holder2.Should().BeNull();

            // 3. GetInfo
            var info = await locks.GetInfo(key);
            info.Should().NotBeNull();
            info!.HolderId.Should().Be(holder.Id);
        }
        finally {
            await holder.DisposeAsync();
        }

        // 4. After release, should be able to lock again
        var holder3 = await locks.TryLock(key);
        holder3.Should().NotBeNull();
        await holder3!.DisposeAsync();
    }

    [Fact(Skip = "For manual testing only with local docker-desktop k8s")]
    public async Task KubeMeshLocks_Lock_Works()
    {
        var locks = Services.GetRequiredService<KubeMeshLocks>();
        var key = "test-lock-wait-" + Guid.NewGuid().ToString("N")[..8];

        var holder1 = await locks.TryLock(key);
        holder1.Should().NotBeNull();

        var lockTask = locks.Lock(key);
        lockTask.IsCompleted.Should().BeFalse();

        await holder1!.DisposeAsync();

        var holder2 = await lockTask.WaitAsync(TimeSpan.FromSeconds(10));
        holder2.Should().NotBeNull();
        await holder2!.DisposeAsync();
    }
}
