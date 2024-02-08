using ActualChat.App.Server;
using ActualChat.Blobs.Internal;
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
    public static FilePath GetManifestPath()
    {
        static FilePath AssemblyPathToManifestPath(FilePath assemblyPath)
        {
            return assemblyPath.ChangeExtension("staticwebassets.runtime.json");
        }

        var hostAssemblyPath = (FilePath)typeof(AppHost).Assembly.Location;
        var manifestPath = AssemblyPathToManifestPath(hostAssemblyPath);
        if (File.Exists(manifestPath))
            return manifestPath;

        throw new FileNotFoundException("Can't find manifest.", manifestPath);
    }

    public static Task<TestAppHost> NewAppHost(
        IMessageSink output,
        string dbInstanceName,
        TestAppHostOptions? options = null)
        => NewAppHost(new TestOutputAdapter(output), dbInstanceName, options);

    public static Task<TestAppHost> NewAppHost(
        ITestOutputHelper output,
        TestAppHostOptions? options = null)
        => NewAppHost(output, GetInstanceName(output), options);

    public static async Task<TestAppHost> NewAppHost(
        ITestOutputHelper output,
        string dbInstanceName,
        TestAppHostOptions? options = null)
    {
        options ??= TestAppHostOptions.Default;
        var manifestPath = GetManifestPath();
        var outputAccessor = new TestOutputHelperAccessor(new TimestampedTestOutput(output));
        var appHost = new TestAppHost(outputAccessor) {
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
                options.AppServicesExtender?.Invoke(host, services);

                // The code below runs after module service registration & everything else
                services.AddSettings<TestSettings>();
                services.AddSingleton(outputAccessor);
                services.ConfigureLogging(outputAccessor);
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
                ConfigureTestApp(cfg, dbInstanceName);
                options.AppConfigurationExtender?.Invoke(cfg);
            },
        };
        appHost.Build();

        if (Constants.DebugMode.Npgsql)
            Npgsql.NpgsqlLoggingConfiguration.InitializeLogging(appHost.Services.GetRequiredService<ILoggerFactory>(), true);
        _ = appHost.Services.GetRequiredService<PostgreSqlPoolCleaner>(); // Force instantiation to ensure it's disposed in the end

        if (options.MustInitializeDb)
            await appHost.InvokeDbInitializers();
        if (options.MustStart)
            await appHost.Start();
        return appHost;
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

    private static string GetInstanceName(ITestOutputHelper output)
        => output.GetTest().TestCase.Traits.GetValueOrDefault("Category")?.FirstOrDefault() ?? "Test";
}
