using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using ActualChat.App.Server;
using ActualChat.Kubernetes.Api;
using ActualChat.Testing.Host;
using Polly;
using Polly.Extensions.Http;

namespace ActualChat.Kubernetes.IntegrationTests;

public class KubeLocksTest(ITestOutputHelper @out) : TestBase(@out)
{
    private IServiceProvider Services { get; set; } = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(KubeInfo.GetLocal)
            .AddSingleton<IKubeInfo>(c => c.GetRequiredService<KubeInfo>())
            .AddSingleton<KubeLeaseClient>()
            .AddSingleton<ILoggerFactory>(c => new LoggerFactory().AddXUnit(@out))
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

    private Task<bool> IsKubeAvailable()
        => Task.FromResult(IKubeInfo.HasKube());

    [Fact]
    public async Task KubeLeaseClient_Crud_Works()
    {
        if (!await IsKubeAvailable()) return;

        var client = Services.GetRequiredService<KubeLeaseClient>();
        var ns = Environment.GetEnvironmentVariable("POD_NAMESPACE") ?? "default";
        var name = "test-lease-" + Guid.NewGuid().ToString("N")[..8];
        var now = Services.Clocks().SystemClock.Now;

        try {
            // 1. Create
            var lease = new Lease(
                new Metadata(name, ns) {
                    Labels = new Labels {
                        ["custom-label"] = "custom-value",
                        App = "test-app",
                    }
                },
                new LeaseSpec("holder-1", 30, now, now)
            );
            var createdLease = await client.Create(ns, lease);
            createdLease.Metadata.Name.Should().Be(name);
            createdLease.Spec.HolderIdentity.Should().Be("holder-1");
            createdLease.Metadata.Labels.Should().NotBeNull();
            createdLease.Metadata.Labels!["custom-label"].Should().Be("custom-value");
            createdLease.Metadata.Labels.App.Should().Be("test-app");

            // 2. Get
            var gotLease = await client.Get(ns, name);
            gotLease.Should().NotBeNull();
            gotLease!.Spec.HolderIdentity.Should().Be("holder-1");
            gotLease.Spec.LeaseDurationSeconds.Should().Be(30);
            gotLease.Spec.AcquireTime.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
            gotLease.Spec.RenewTime.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));

            // 3. Replace
            var now2 = Services.Clocks().SystemClock.Now;
            gotLease = gotLease with {
                Spec = gotLease.Spec with {
                    HolderIdentity = "holder-2",
                    RenewTime = now2,
                }
            };
            var replacedLease = await client.Replace(ns, gotLease);
            replacedLease.Spec.HolderIdentity.Should().Be("holder-2");
            replacedLease.Spec.RenewTime.Should().BeCloseTo(now2, TimeSpan.FromSeconds(1));

            // 4. List
            var list = await client.List(ns);
            list.Items.Should().Contain(x => x.Metadata.Name == name);

            // 5. Delete
            var deleted = await client.Delete(ns, name);
            deleted.Should().BeTrue();

            gotLease = await client.Get(ns, name);
            gotLease.Should().BeNull();
        }
        finally {
            await client.Delete(ns, name);
        }
    }

    [Fact/*(Skip = "For manual testing only")*/]
    public async Task KubeMeshLocks_Basic_Works()
    {
        if (!await IsKubeAvailable()) return;

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

    [Fact/*(Skip = "For manual testing only")*/]
    public async Task KubeMeshLocks_Lock_Works()
    {
        if (!await IsKubeAvailable()) return;

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

    [Fact]
    public async Task KubeMeshLocks_Create_AppHost()
    {
        var appHost = await TestAppHostFactory.NewAppHost(TestAppHostOptions.Default.With("KubeMeshLocks", @out));
        appHost.Should().NotBeNull();
    }
}
