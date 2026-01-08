using ActualChat.Kubernetes.Api;
using ActualChat.Testing.Host;

namespace ActualChat.Kubernetes.IntegrationTests;

public class KubeLocksTest(ITestOutputHelper @out) : AppHostTestBase("KubeLocks", @out)
{
    private TestAppHost? _appHost;
    private IServiceProvider Services => _appHost!.Services;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        _appHost = await NewAppHost(options => options with {
            ConfigureServices = (_, services) => {
                services.AddSingleton(KubeInfo.GetLocal);
            },
        });
        var kubeInfo = Services.GetRequiredService<KubeInfo>();
        if (!await kubeInfo.HasKube())
            WriteLine("Kubernetes is not available, skipping tests.");
    }

    protected override async Task DisposeAsync()
    {
        if (_appHost != null)
            await _appHost.DisposeAsync();
        await base.DisposeAsync();
    }

    private async Task<bool> IsKubeAvailable()
    {
        if (_appHost == null) return false;
        var kubeInfo = Services.GetRequiredService<KubeInfo>();
        return await kubeInfo.HasKube();
    }

    [Fact(Skip = "For manual testing only")]
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
                new Metadata(name, ns),
                new LeaseSpec("holder-1", 30, now, now)
            );
            var createdLease = await client.Create(ns, lease);
            createdLease.Metadata.Name.Should().Be(name);
            createdLease.Spec.HolderIdentity.Should().Be("holder-1");

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

    [Fact(Skip = "For manual testing only")]
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

    [Fact(Skip = "For manual testing only")]
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
}
