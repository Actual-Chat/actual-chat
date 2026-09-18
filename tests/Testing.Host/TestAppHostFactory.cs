using System.Net;
using System.Net.Sockets;
using ActualChat.App.Server;
using ActualChat.App.Server.Module;
using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
using ActualChat.MLSearch.Engine;
using ActualChat.Module;
using ActualChat.Notifications;
using ActualChat.Transcription;
using ActualChat.Transcription.Module;
using ActualLab.IO;
using ActualLab.Testing.Web;
using DotNetEnv.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit.DependencyInjection;

namespace ActualChat.Testing.Host;

public static class TestAppHostFactory
{
    private const int BindAttemptCount = 3;

    public static async Task<TestAppHost> NewAppHost(TestAppHostOptions options)
    {
        // The backstop for a port that something outside the test run took anyway: the port is baked into
        // the host's configuration, so a new one means building the host again.
        for (var attempt = 1;; attempt++) {
            try {
                return await NewAppHostOnce(options).ConfigureAwait(false);
            }
            catch (Exception e) when (options.ServerUrls == null
                && attempt < BindAttemptCount
                && IsAddressInUse(e)) {
                // NewAppHostOnce logged it and disposed the half-built host; the next attempt picks a new port
            }
        }
    }

    private static async Task<TestAppHost> NewAppHostOnce(TestAppHostOptions options)
    {
        var instanceName = options.InstanceName.RequireNonEmpty();
        var testOutputHelper = options.Output.ToSafe();
        var outputAccessor = new TestOutputHelperAccessor() { Output = testOutputHelper };
        var log = outputAccessor.CreateTestLoggerFactory().CreateLogger(nameof(TestAppHostFactory));
        log.LogInformation("-> NewAppHost, instance '{InstanceName}'", instanceName);
        var manifestPath = GetManifestPath();

        // GetUnusedTcpPort closes its listener before returning, so the port is only reserved for as long
        // as it takes the OS to hand it to someone else. Keep it bound until Kestrel is ready to take it:
        // while this listener lives, no other request for a free port can be answered with the same one.
        TcpListener? portHolder = null;
        string serverUrls;
        if (options.ServerUrls is { } configuredUrls)
            serverUrls = configuredUrls;
        else {
            portHolder = new TcpListener(IPAddress.Any, 0);
            portHolder.Start();
            serverUrls = WebTestHelpers.GetLocalUri(((IPEndPoint)portHolder.LocalEndpoint).Port).ToString();
        }
        var appHost = new TestAppHost(options, outputAccessor) {
            ServerUrls = serverUrls,
            HostOptions = new() {
                EnvironmentName = Environments.Development,
            },
            ConfigureHost = (ctx, cfg) => {
                // Removing default appsettings.* and DotNetEnv
                var toDelete = cfg.Sources
                    .Where(s =>
                        (s is JsonConfigurationSource source
                            && (source.Path ?? "").StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
                        || s is EnvConfigurationSource
                        || s is EnvironmentVariablesConfigurationSource
                    ).ToList();
                foreach (var source in toDelete)
                    cfg.Sources.Remove(source);

                // Adding testsettings.* instead
                var fileProvider = new PhysicalFileProvider(Path.GetDirectoryName(typeof(TestBase).Assembly.Location));
                foreach (var (fileName, optional) in GetTestSettingsFiles())
                    cfg.Sources.Add(new JsonConfigurationSource {
                        FileProvider = fileProvider,
                        OnLoadException = null,
                        Optional = optional,
                        Path = fileName,
                        ReloadDelay = 100,
                        ReloadOnChange = false,
                    });
                cfg.AddEnvironmentVariables();

                // Adding must-have overrides for tests
                var useNatsQueues = options.UseNatsQueues ?? true; // Random.Shared.NextDouble() < 0.33;
                cfg.AddInMemoryCollection(
                        (WebHostDefaults.StaticWebAssetsKey, manifestPath),
                        ($"{nameof(CoreSettings)}:{nameof(CoreServerSettings.UseNatsQueues)}", $"{useNatsQueues}"))
                    .AddInMemory<CoreSettings>((x => x.Instance, instanceName))
                    .AddInMemory<HostSettings>(
                        (x => x.BaseUri, serverUrls),
                        (x => x.MeshLockSubspace, options.MeshLockSubspace),
                        (x => x.MeshLockOptionsPreset, options.MeshLockOptionsPreset));
                // Ensure random port overrides any BasePort from .env
                cfg.AddInMemoryCollection([new(WebHostDefaults.ServerUrlsKey, serverUrls)]);

                // Overrides from options
                options.ConfigureHost?.Invoke(ctx, cfg);
            },
            ConfigureModuleServices = (ctx, services) => {
                // Overrides from options
                options.ConfigureModuleServices?.Invoke(ctx, services);

                services.AddSingleton(outputAccessor);
                services.AddTransient<ITestOutputHelper>(_ => outputAccessor.Output ?? NullTestOutput.Instance);
                services.AddTestLogging(outputAccessor);
            },
            ConfigureServices = (ctx, services) => {
                // The code below runs after module service registration & everything else
                services.AddSettings<TestSettings>();
                services.AddSingleton(options.DbInitializeOptions);
                services.AddSingleton(options.ChatDbInitializerOptions);
                services.AddSingleton<IBlobStorages, TempFolderBlobStorages>();
                services.AddSingleton<PostgreSqlPoolCleaner>();
                services.AddSingleton<OpenSearchNames>(_ => new OpenSearchNames {
                    TestIsolationKey = UniqueNames.Random(),
                    Env = OpenSearchNames.TestPrefix,
                });
                services.AddTestLogging(outputAccessor);

                // Record pushes instead of hitting Firebase
                services.AddSingleton<FirebaseMessagingTestSink>();
                services.AddSingleton<IFirebaseMessagingClient>(
                    c => c.GetRequiredService<FirebaseMessagingTestSink>());
                services.AddSingleton<ApnsTestSink>();
                services.AddSingleton<IApnsClient>(
                    c => c.GetRequiredService<ApnsTestSink>());
                services.AddSingleton(new MeshWatcherOptions { MustAnnounceAfterHostStart = options.MustStart });

                // AddSoniox (and its ISonioxVoices registration) is skipped when UseFakeTranscriber is set
                if (ctx.Cfg.Settings<TranscriptionSettings>().UseFakeTranscriber) {
                    services.AddSingleton<FakeSonioxVoices>();
                    services.AddSingleton<ISonioxVoices>(c => c.GetRequiredService<FakeSonioxVoices>());
                }

                // Overrides from options
                options.ConfigureServices?.Invoke(ctx, services);
            },
            ConfigureApp = (ctx, app) => options.ConfigureApp?.Invoke(ctx, app),
        };
        try {
            appHost.Build();
            log.LogInformation("-- NewAppHost has built, instance '{InstanceName}'", instanceName);

            if (Constants.DebugMode.Npgsql)
                Npgsql.NpgsqlLoggingConfiguration.InitializeLogging(appHost.Services.GetRequiredService<ILoggerFactory>(), true);
            _ = appHost.Services.GetRequiredService<PostgreSqlPoolCleaner>(); // Force instantiation to ensure it's disposed in the end

            // Cleanup existing queues
            await appHost.Services.Queues().PurgeWithTimeout(
                TimeSpan.FromSeconds(10),
                msg => log.LogWarning("{Message}", msg));

            if (options.MustInitializeDb)
                await appHost.RunInitializers();

            portHolder?.Stop();
            if (options.MustStart)
                await appHost.Start();
        }
        catch (Exception e) {
            log.LogWarning(e, "-- NewAppHost failed, instance '{InstanceName}'", instanceName);
            await appHost.DisposeSilentlyAsync().ConfigureAwait(false);
            throw;
        }
        finally {
            portHolder?.Stop();
        }

        log.LogInformation("<- NewAppHost, instance '{InstanceName}'", instanceName);
        return appHost;
    }

    // Private methods

    private static bool IsAddressInUse(Exception e)
    {
        for (var current = (Exception?)e; current != null; current = current.InnerException)
            if (current is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
                return true;
        return false;
    }

    private static FilePath GetManifestPath()
    {
        var hostAssemblyPath = (FilePath)typeof(AppHost).Assembly.Location;
        var manifestPath = AssemblyPathToManifestPath(hostAssemblyPath);
        return File.Exists(manifestPath)
            ? manifestPath
            : throw new FileNotFoundException("Can't find manifest.", manifestPath);

        static FilePath AssemblyPathToManifestPath(FilePath assemblyPath)
            => assemblyPath.ChangeExtension("staticwebassets.runtime.json");
    }

    private static List<(string FileName, bool Optional)> GetTestSettingsFiles()
    {
         var result = new List<(string FileName, bool Optional)> {
            ("testsettings.json", Optional: false),
            ("testsettings.local.json", Optional: true),
        };
        if (EnvExt.IsRunningInContainer())
            result.Add(("testsettings.docker.json", Optional: false));
        if (EnvExt.IsAgentMode())
            result.Add(("testsettings.agent.json", Optional: true));
        return result;
    }
}
