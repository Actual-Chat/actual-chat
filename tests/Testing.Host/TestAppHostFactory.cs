using ActualChat.App.Server;
using ActualChat.App.Server.Module;
using ActualChat.Blobs.Internal;
using ActualChat.MLSearch.Engine;
using ActualChat.Module;
using ActualChat.Notifications;
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
    public static async Task<TestAppHost> NewAppHost(TestAppHostOptions options)
    {
        var instanceName = options.InstanceName.RequireNonEmpty();
        var testOutputHelper = options.Output.ToSafe();
        var outputAccessor = new TestOutputHelperAccessor() { Output = testOutputHelper };
        var log = outputAccessor.CreateTestLoggerFactory().CreateLogger(nameof(TestAppHostFactory));
        log.LogInformation("-> NewAppHost, instance '{InstanceName}'", instanceName);
        var manifestPath = GetManifestPath();

        var serverUrls = options.ServerUrls ?? WebTestHelpers.GetUnusedLocalUri().ToString();
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

                // Overrides from options
                options.ConfigureServices?.Invoke(ctx, services);
            },
            ConfigureApp = (ctx, app) => options.ConfigureApp?.Invoke(ctx, app),
        };
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

        if (options.MustStart)
            await appHost.Start();

        log.LogInformation("<- NewAppHost, instance '{InstanceName}'", instanceName);
        return appHost;
    }

    // Private methods

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
