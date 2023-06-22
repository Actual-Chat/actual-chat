using ActualChat.App.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.FileProviders;
using Stl.Fusion.Server;
using Stl.Fusion.Server.Authentication;
using Stl.IO;
using Stl.Testing.Output;
using Xunit.DependencyInjection.Logging;

namespace ActualChat.Testing.Host;

public static class TestHostFactory
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
        Action<IConfigurationBuilder>? configureAppSettings = null,
        Action<IServiceCollection>? configureServices = null,
        string? serverUrls = null)
    {
        var manifestPath = GetManifestPath();
        var appHost = new AppHost {
            ServerUrls = serverUrls ?? WebTestExt.GetLocalUri(WebTestExt.GetUnusedTcpPort()).ToString(),
            HostConfigurationBuilder = cfg => {
                cfg.Sources.Insert(0,
                    new MemoryConfigurationSource {
                        InitialData = new Dictionary<string, string?> {
                            { WebHostDefaults.EnvironmentKey, "Development" },
                            { WebHostDefaults.StaticWebAssetsKey, manifestPath },
                        },
                    });
            },
            AppServicesBuilder = (host, services) => {
                configureServices?.Invoke(services);
                services.ConfigureLogging(output);
                services.AddSingleton(new ServerAuthHelper.Options {
                    KeepSignedIn = true,
                });
                services.AddSettings<TestSettings>();
                services.AddSingleton(output);
                services.AddSingleton<PostgreSqlPoolCleaner>();
                var fusion = services.AddFusion();
                var fusionServer = fusion.AddWebServer();
                fusionServer.AddSessionMiddleware();
            },
            AppConfigurationBuilder = builder => {
                ConfigureTestApp(builder, output);
                configureAppSettings?.Invoke(builder);
            },
        };
        await appHost.Build();
        await appHost.InvokeDbInitializers();
        _ = appHost.Services.GetRequiredService<PostgreSqlPoolCleaner>(); // force service instantiation
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
