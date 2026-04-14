using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using ActualChat.Kubernetes.Api;

namespace ActualChat.Kubernetes.IntegrationTests;

public class KubeLeaseClientTest(ITestOutputHelper @out) : TestBase(@out)
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
            .AddSingleton(new MeshLockRenewalThreads(1))
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
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));
        Services = serviceCollection.BuildServiceProvider();
    }

    protected override async Task DisposeAsync()
    {
        if (Services is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        await base.DisposeAsync();
    }

    [Fact(Skip = "For manual testing only with local docker-desktop k8s")]
    public async Task KubeLeaseClient_Crud_Works()
    {
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
                    },
                    Annotations = new Annotations {
                        ["custom-annotation"] = "custom-value-2",
                    },
                },
                new LeaseSpec("holder-1", 30, now, now)
            );
            var createdLease = await client.Create(ns, lease);
            createdLease.Metadata.Name.Should().Be(name);
            createdLease.Spec.HolderIdentity.Should().Be("holder-1");
            createdLease.Metadata.Labels.Should().NotBeNull();
            createdLease.Metadata.Labels!["custom-label"].Should().Be("custom-value");
            createdLease.Metadata.Labels.App.Should().Be("test-app");
            createdLease.Metadata.Annotations.Should().NotBeNull();
            createdLease.Metadata.Annotations!["custom-annotation"].Should().Be("custom-value-2");

            // 2. Get
            var gotLease = await client.Get(ns, name);
            gotLease.Should().NotBeNull();
            gotLease!.Spec.HolderIdentity.Should().Be("holder-1");
            gotLease.Spec.LeaseDurationSeconds.Should().Be(30);
            gotLease.Spec.AcquireTime.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
            gotLease.Spec.RenewTime.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
            gotLease.Spec.LeaseTransitions.Should().BeNull();
            gotLease.Metadata.Labels.Should().NotBeNull();
            gotLease.Metadata.Labels!["custom-label"].Should().Be("custom-value");
            gotLease.Metadata.Annotations.Should().NotBeNull();
            gotLease.Metadata.Annotations!["custom-annotation"].Should().Be("custom-value-2");

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

    [Fact(Skip = "For manual testing only with local docker-desktop k8s")]
    public async Task KubeLeaseClient_Watch_Works()
    {
        var client = Services.GetRequiredService<KubeLeaseClient>();
        var ns = Environment.GetEnvironmentVariable("POD_NAMESPACE") ?? "default";
        var name = "test-watch-" + Guid.NewGuid().ToString("N")[..8];
        var now = Services.Clocks().SystemClock.Now;
        var labelSelector = $"test-watch={name}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var channel = Channel.CreateUnbounded<ActualChat.Kubernetes.Api.Change<Lease>>();

        var cancellationToken = cts.Token;
        var watchTask = Task.Run(async () => await client.Watch(
            ns,
            labelSelector,
            (change, ct) => {
                channel.Writer.TryWrite(change);
                return Task.CompletedTask;
            },
            cancellationToken: cancellationToken
        ), cancellationToken);

        try {
            // 1. Create
            var lease = new Lease(
                new Metadata(name, ns) {
                    Labels = new Labels {
                        ["test-watch"] = name,
                        App = "test-app",
                    }
                },
                new LeaseSpec("holder-1", 3, now, now)
            );
            await client.Create(ns, lease, null, cancellationToken);
            Out.WriteLine($"Created lease {name}");

            var change = await channel.Reader.ReadAsync(cancellationToken);
            change.Type.Should().Be(ChangeType.Added);
            change.Object.Metadata.Name.Should().Be(name);
            Out.WriteLine($"Change create {name} has been captured");

            // 2. Delete
            await client.Delete(ns, name, null, cancellationToken);
            Out.WriteLine($"Deleted lease {name}");

            change = await channel.Reader.ReadAsync(cancellationToken);
            change.Type.Should().Be(ChangeType.Deleted);
            change.Object.Metadata.Name.Should().Be(name);
            Out.WriteLine($"Change delete {name} has been captured");
        }
        finally {
            Out.WriteLine("Cleaning up...");
            await cts.CancelAsync();
            Out.WriteLine("Cleaned up.");
            try {
                await watchTask;
            }
            catch (OperationCanceledException) { }
        }
    }
}
