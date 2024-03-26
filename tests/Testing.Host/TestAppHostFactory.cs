using ActualChat.App.Server;
using ActualChat.Blobs.Internal;
using ActualChat.Queues.Nats;
using ActualChat.Search;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using ActualLab.IO;
using ActualLab.Testing.Output;

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
            ConfigureHost = (builder, cfg) => {
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
                cfg.AddInMemoryCollection(new Dictionary<string, string?> {
                    { WebHostDefaults.EnvironmentKey, "Development" },
                    { WebHostDefaults.StaticWebAssetsKey, manifestPath },
                    { "CoreSettings:Instance", instanceName },
                });

                // Overrides from options
                options.ConfigureHost?.Invoke(builder, cfg);
            },
            ConfigureServices = (builder, services) => {
                // Overrides from options
                options.ConfigureAppServices?.Invoke(builder, services);

                // The code below runs after module service registration & everything else
                services.AddSettings<TestSettings>();
                services.AddSingleton(outputAccessor);
                services.AddTestLogging(outputAccessor);
                services.AddSingleton(options.ChatDbInitializerOptions);
                services.AddSingleton<IBlobStorages, TempFolderBlobStorages>();
                services.AddSingleton<PostgreSqlPoolCleaner>();
                services.AddSingleton(new NatsQueues.Options {
                    InstanceName = instanceName,
                });
                services.AddSingleton<ElasticNames>(_ => new ElasticNames {
                    IndexPrefix = UniqueNames.Elastic("test"),
                });
            },
            ConfigureApp = (builder, app) => options.ConfigureApp?.Invoke(builder, app),
        };
        appHost.Build();

        if (Constants.DebugMode.Npgsql)
            Npgsql.NpgsqlLoggingConfiguration.InitializeLogging(appHost.Services.GetRequiredService<ILoggerFactory>(), true);
        _ = appHost.Services.GetRequiredService<PostgreSqlPoolCleaner>(); // Force instantiation to ensure it's disposed in the end

        if (options.MustInitializeDb)
            // TODO: Improve initializers init code.
            // Issue: Not granular or too specific.
            await appHost.InvokeInitializers();

        // Cleanup existing queues
        await appHost.Services.Queues().Purge();

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
