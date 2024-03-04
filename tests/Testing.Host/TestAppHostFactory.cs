using ActualChat.App.Server;
using ActualChat.Blobs.Internal;
using ActualChat.Commands;
using ActualChat.Nats;
using ActualChat.Search;
using ActualLab.Generators;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
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
            HostConfigurationBuilder = cfg => {
                cfg.Sources.Insert(0,
                    new MemoryConfigurationSource {
                        InitialData = new Dictionary<string, string?> {
                            { WebHostDefaults.EnvironmentKey, "Development" },
                            { WebHostDefaults.StaticWebAssetsKey, manifestPath },
                        },
                    });
                options.HostConfigurationExtender?.Invoke(cfg);
            },
            AppServicesBuilder = (host, services) => {
                // register prefix for NATS queues
                services.AddSingleton(new NatsCommandQueues.Options {
                    CommonPrefix = instanceName,
                });

                options.AppServicesExtender?.Invoke(host, services);

                // The code below runs after module service registration & everything else
                services.AddSettings<TestSettings>();
                services.AddSingleton(outputAccessor);
                services.AddTestLogging(outputAccessor);
                services.AddSingleton(options.ChatDbInitializerOptions);
                services.AddSingleton<IBlobStorages, TempFolderBlobStorages>();
                services.AddSingleton<PostgreSqlPoolCleaner>();
                services.AddSingleton<ElasticNames>(_ => {
                    var rsg = new RandomStringGenerator(6, RandomStringGenerator.Base32Alphabet);
                    return new () {
                        IndexPrefix = "test-" + rsg.Next(6).ToLowerInvariant() + "-",
                    };
                });
            },
            AppConfigurationBuilder = cfg => {
                ConfigureTestApp(cfg, instanceName);
                options.AppConfigurationExtender?.Invoke(cfg);
            },
        };
        appHost.Build();
        // Cleanup existing queues
        var queues = appHost.Services.GetRequiredService<ICommandQueues>();
        await queues.Purge(CancellationToken.None);

        if (Constants.DebugMode.Npgsql)
            Npgsql.NpgsqlLoggingConfiguration.InitializeLogging(appHost.Services.GetRequiredService<ILoggerFactory>(), true);
        _ = appHost.Services.GetRequiredService<PostgreSqlPoolCleaner>(); // Force instantiation to ensure it's disposed in the end

        if (options.MustInitializeDb)
            await appHost.InvokeDbInitializers();
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

    private static void ConfigureTestApp(IConfigurationBuilder config, string instanceName)
    {
        var toDelete = config.Sources
            .Where(s => (s is JsonConfigurationSource source
                && (source.Path ?? "").StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
                || s is EnvironmentVariablesConfigurationSource)
            .ToList();
        foreach (var source in toDelete)
            config.Sources.Remove(source);

        var fileProvider = new PhysicalFileProvider(Path.GetDirectoryName(typeof(TestBase).Assembly.Location));
        foreach (var (fileName, optional) in GetTestSettingsFiles())
            config.Sources.Add(new JsonConfigurationSource {
                FileProvider = fileProvider,
                OnLoadException = null,
                Optional = optional,
                Path = fileName,
                ReloadDelay = 100,
                ReloadOnChange = false,
            });
        config.AddInMemoryCollection(new Dictionary<string, string?> {
            { "CoreSettings:Instance", instanceName },
        });
        config.AddEnvironmentVariables();

        static List<(string FileName, bool Optional)> GetTestSettingsFiles()
        {
            List<(string FileName, bool Optional)> result = new (3) {
                ("testsettings.json", Optional: false),
                ("testsettings.local.json", Optional: true),
            };
            if (EnvExt.IsRunningInContainer())
                result.Add(("testsettings.docker.json", Optional: false));
            return result;
        }
    }
}
