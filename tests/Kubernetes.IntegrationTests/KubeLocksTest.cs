using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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
        // Ensure current context is docker-desktop
        ExecuteCommand("kubectl", "config use-context docker-desktop");
        // Ensure the default service account has the required permissions for Leases
        SetupKubePermissions();

        _appHost = await NewAppHost(options => options with {
            ConfigureServices = (ctx, services) => {
                var tempDir = Path.GetTempPath();
                var tokenPath = Path.Combine(tempDir, "kube-token");
                var caPath = Path.Combine(tempDir, "kube-ca.crt");

                // Try to get token and CA from kubectl
                try {
                    var token = ExecuteCommand("kubectl", "create token default --duration=24h").Trim();
                    if (!string.IsNullOrEmpty(token))
                        File.WriteAllText(tokenPath, token);

                    var caData = ExecuteCommand("kubectl", "config view --raw -o jsonpath=\"{.clusters[?(@.name=='docker-desktop')].cluster.certificate-authority-data}\"").Trim();
                    if (!string.IsNullOrEmpty(caData)) {
                        File.WriteAllBytes(caPath, Convert.FromBase64String(caData));
                    }
                }
                catch (Exception e) {
                    WriteLine($"Failed to get kube config via kubectl: {e.Message}");
                }

                Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", "kubernetes.docker.internal");
                Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_PORT", "6443");
                Environment.SetEnvironmentVariable("POD_IP", "127.0.0.1");
                Environment.SetEnvironmentVariable("POD_NAMESPACE", "default");

                services.AddSingleton(c => new KubeInfo(c) {
                    TokenPath = tokenPath,
                    CACertPath = caPath,
                });
                services.AddSingleton<IKubeInfo>(c => c.GetRequiredService<KubeInfo>());
                services.AddSingleton<KubeServices>();
                services.AddSingleton<KubeLeaseClient>();
                services.AddSingleton<KubeMeshLocks>();
                services.AddSingleton(typeof(KubeMeshLocks<>));
                services.AddHttpClient(Kube.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(c => {
                        var handler = new HttpClientHandler();
                        var kubeInfo = c.GetRequiredService<KubeInfo>();
                        if (File.Exists(kubeInfo.CACertPath)) {
                            var caCertString = File.ReadAllText(kubeInfo.CACertPath);
                            var caCert = X509Certificate2.CreateFromPem(caCertString);
                            handler.ServerCertificateCustomValidationCallback = (_, cert, _, policyErrors) => {
                                if (cert == null) return false;
                                if (policyErrors != SslPolicyErrors.RemoteCertificateChainErrors) return false;
                                try {
                                    using var x509Chain = new X509Chain();
                                    x509Chain.ChainPolicy.ExtraStore.Add(caCert);
                                    x509Chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                                    x509Chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                                    return x509Chain.Build(cert);
                                }
                                catch { return false; }
                            };
                        }
                        return handler;
                    });
            }
        });
        var kubeInfo = Services.GetRequiredService<KubeInfo>();
        if (!await kubeInfo.HasKube()) {
            WriteLine("Kubernetes is not available, skipping tests.");
            return;
        }
    }

    private static string ExecuteCommand(string command, string arguments)
    {
        var startInfo = new ProcessStartInfo {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(startInfo);
        var output = process?.StandardOutput.ReadToEnd() ?? "";
        process?.WaitForExit();
        return output;
    }

    private void SetupKubePermissions()
    {
        try {
            // Create a Role for managing leases in the default namespace
            var roleYaml = """
                           apiVersion: rbac.authorization.k8s.io/v1
                           kind: Role
                           metadata:
                             namespace: default
                             name: lease-manager
                           rules:
                           - apiGroups: ["coordination.k8s.io"]
                             resources: ["leases"]
                             verbs: ["get", "list", "watch", "create", "update", "patch", "delete"]
                           - apiGroups: ["discovery.k8s.io"]
                             resources: ["endpointslices"]
                             verbs: ["get", "list", "watch"]
                           """;
            var roleFile = Path.Combine(Path.GetTempPath(), "lease-manager-role.yaml");
            File.WriteAllText(roleFile, roleYaml);
            ExecuteCommand("kubectl", $"apply -f {roleFile}");

            // Bind the Role to the default ServiceAccount in the default namespace
            var bindingYaml = """
                              apiVersion: rbac.authorization.k8s.io/v1
                              kind: RoleBinding
                              metadata:
                                namespace: default
                                name: lease-manager-binding
                              subjects:
                              - kind: ServiceAccount
                                name: default
                                namespace: default
                              roleRef:
                                kind: Role
                                name: lease-manager
                                apiGroup: rbac.authorization.k8s.io
                              """;
            var bindingFile = Path.Combine(Path.GetTempPath(), "lease-manager-binding.yaml");
            File.WriteAllText(bindingFile, bindingYaml);
            ExecuteCommand("kubectl", $"apply -f {bindingFile}");
        }
        catch (Exception e) {
            WriteLine($"Failed to setup kube permissions: {e.Message}");
        }
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

    [Fact]
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

    [Fact]
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
