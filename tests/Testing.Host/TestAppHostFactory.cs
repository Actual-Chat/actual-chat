using ActualChat.App.Server;
using ActualChat.Chat.Module;
using ActualChat.Blobs.Internal;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.FileProviders;
using ActualLab.Fusion.Server.Authentication;
using ActualLab.IO;

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

    public static async Task<AppHost> NewAppHost(
        ITestOutputHelper output,
        TestAppHostOptions? options = null)
    {
        options ??= TestAppHostOptions.Default;
        var manifestPath = GetManifestPath();
        var appHost = new TestAppHost {
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
                services.AddSingleton(output);
                services.ConfigureLogging(output);
                services.AddSingleton(new ServerAuthHelper.Options {
                    KeepSignedIn = true,
                });
                services.AddSingleton(options.ChatDbInitializerOptions);
                services.AddSingleton<IBlobStorageProvider, TempFolderBlobStorageProvider>();
                services.AddSingleton<PostgreSqlPoolCleaner>();
            },
            AppConfigurationBuilder = cfg => {
                ConfigureTestApp(cfg, output);
                options.AppConfigurationExtender?.Invoke(cfg);
            },
        };
        await appHost.Build();

        if (Constants.DebugMode.Npgsql)
            Npgsql.NpgsqlLoggingConfiguration.InitializeLogging(appHost.Services.GetRequiredService<ILoggerFactory>(), true);
        _ = appHost.Services.GetRequiredService<PostgreSqlPoolCleaner>(); // Force instantiation to ensure it's disposed in the end

        if (options.MustInitializeDb)
            await appHost.InvokeDbInitializers();
        if (options.MustStart)
            await appHost.Start();
        return appHost;
    }

    private static void ConfigureTestApp(IConfigurationBuilder config, ITestOutputHelper output)
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
            { "CoreSettings:Instance", GetInstanceName(output) },
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
    {
        var test = output.GetTest();
        // Postgres identifier limit is 63 bytes
        var displayName = test.DisplayName;
        // On build server displayName is generated based on class full name and method name,
        // while in Rider only method name is used.
        // Drop namespace to have more readable instance name (with test method name) after length is truncated.
        var classNamespace = test.TestCase.TestMethod.TestClass.Class.ToRuntimeType().Namespace;
        if (displayName.OrdinalStartsWith(classNamespace))
            displayName = displayName.Substring(classNamespace.Length + 1);
        return FilePath.GetHashedName(test.TestCase.UniqueID, displayName);
    }
}
