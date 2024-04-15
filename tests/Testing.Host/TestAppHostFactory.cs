using ActualChat.App.Server;
using ActualChat.Blobs.Internal;
using ActualChat.Module;
using ActualChat.Redis;
using ActualChat.Search;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using ActualLab.IO;
using ActualLab.Testing.Output;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Testing.Host;

public static class TestAppHostFactory
{
    public static async Task<TestAppHost> NewAppHost(TestAppHostOptions options)
    {
        var instanceName = options.InstanceName.RequireNonEmpty();
        var outputAccessor = new TestOutputHelperAccessor(options.Output.ToSafe());
        var manifestPath = GetManifestPath();

        var appHost = new TestAppHost(options, outputAccessor) {
            ServerUrls = options.ServerUrls ?? WebTestExt.GetLocalUri(WebTestExt.GetUnusedTcpPort()).ToString(),
            HostOptions = new() {
                EnvironmentName = Environments.Development,
            },
            ConfigureHost = (ctx, cfg) => {
                // Removing default appsettings.*
                var toDelete = cfg.Sources
                    .Where(s => (s is JsonConfigurationSource source
                        && (source.Path ?? "").StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
                        || s is EnvironmentVariablesConfigurationSource)
                    .ToList();
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

                // Adding must-have overrides for tests
                var useNatsQueues = options.UseNatsQueues ?? true; // Random.Shared.NextDouble() < 0.33;
                cfg.AddInMemoryCollection(
                    (WebHostDefaults.StaticWebAssetsKey, manifestPath),
                    ($"{nameof(CoreSettings)}:{nameof(CoreSettings.Instance)}", instanceName),
                    ($"{nameof(CoreSettings)}:{nameof(CoreServerSettings.UseNatsQueues)}", useNatsQueues.ToString())
                );

                // Overrides from options
                options.ConfigureHost?.Invoke(ctx, cfg);
            },
            ConfigureModuleServices = (ctx, services) => {
                // Overrides from options
                options.ConfigureModuleServices?.Invoke(ctx, services);

                services.AddSingleton(outputAccessor);
                services.AddTestLogging(outputAccessor);
            },
            ConfigureServices = (ctx, services) => {
                // Overrides from options
                options.ConfigureServices?.Invoke(ctx, services);

                // The code below runs after module service registration & everything else
                services.AddSettings<TestSettings>();
                services.AddSingleton(options.DbInitializeOptions);
                services.AddSingleton(options.ChatDbInitializerOptions);
                services.AddSingleton<IBlobStorages, TempFolderBlobStorages>();
                services.AddSingleton<PostgreSqlPoolCleaner>();
                services.AddSingleton<IndexNames>(_ => new IndexNames {
                    IndexPrefix = UniqueNames.Elastic("test"),
                });
            },
            ConfigureApp = (ctx, app) => options.ConfigureApp?.Invoke(ctx, app),
        };
        appHost.Build();

        if (Constants.DebugMode.Npgsql)
            Npgsql.NpgsqlLoggingConfiguration.InitializeLogging(appHost.Services.GetRequiredService<ILoggerFactory>(), true);
        _ = appHost.Services.GetRequiredService<PostgreSqlPoolCleaner>(); // Force instantiation to ensure it's disposed in the end

        // Clean up infrastructure MeshLocks
        var meshLocks = appHost.Services.MeshLocks<InfrastructureDbContext>();
        if (options.MustCleanupRedis && meshLocks is RedisMeshLocks redisMeshLocks) {
            var keyCount = await redisMeshLocks.RemoveKeys("*");
            outputAccessor.Output?.WriteLine($"Removed {keyCount} Redis keys.");
        }

        // Cleanup existing queues
        await appHost.Services.Queues().Purge();

        if (options.MustInitializeDb)
            // TODO: Improve initializers init code.
            // Issue: Not granular or too specific.
            await appHost.InvokeInitializers();

        if (options.MustStart)
            await appHost.Start();

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
        return result;
    }
}
