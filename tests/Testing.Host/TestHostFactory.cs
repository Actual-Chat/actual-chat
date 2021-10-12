using ActualChat.Host;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Stl.Collections;
using Stl.Fusion.Server.Authentication;
using Stl.IO;

namespace ActualChat.Testing.Host
{
    public static class TestHostFactory
    {
        private static readonly string[] TestSettingsFiles = new[]
            {"testsettings.json", "testsettings.docker.json", "testsettings.local.json"};

        public static async Task<AppHost> NewAppHost(Action<AppHost>? configure = null)
        {
            var port = WebTestExt.GetUnusedTcpPort();
            var manifestPath = GetManifestPath();
            var appHost = new AppHost() {
                ServerUrls = WebTestExt.GetLocalUri(port).ToString(),
                HostConfigurationBuilder = cfg => {
                    cfg.Sources.Insert(0, new MemoryConfigurationSource() {
                        InitialData = new Dictionary<string, string>() {
                            {WebHostDefaults.EnvironmentKey, "Development"},
                            {WebHostDefaults.StaticWebAssetsKey, manifestPath},
                        }
                    });
                },
                AppServicesBuilder = (_, services) => {
                    services.AddSingleton(new ServerAuthHelper.Options() {
                        KeepSignedIn = true,
                    });
                },
                AppConfigurationBuilder = GetTestAppSettings
            };
            configure?.Invoke(appHost);
            await appHost.Build();
            await appHost.Initialize(true);
            await appHost.Start();
            return appHost;
        }

        public static FilePath GetManifestPath()
        {
            FilePath AssemblyPathToManifestPath(FilePath assemblyPath)
                => assemblyPath.ChangeExtension("staticwebassets.runtime.json");

            var hostAssemblyPath = (FilePath)typeof(AppHost).Assembly.Location;
            var manifestPath = AssemblyPathToManifestPath(hostAssemblyPath);
            if (File.Exists(manifestPath))
                return manifestPath;
            throw new Exception("Can't find manifest.");
        }

        private static void GetTestAppSettings(IConfigurationBuilder config)
        {
            var newSources = new List<IConfigurationSource>();
            var sources = config.Sources;
            foreach (var source in sources) {
                if (source is JsonConfigurationSource b && b.Path.Contains("appsettings"))
                    continue;
                newSources.Add(source);
            }

            sources = newSources;
            var fileProvider = new PhysicalFileProvider(Path.GetDirectoryName(typeof(TestSettings).Assembly.Location));
            foreach (var settingsFile in TestSettingsFiles) {
                if (!File.Exists(Path.Combine(fileProvider.Root, settingsFile)))
                    continue;
                var (addToSources, optionalField) = CheckSettingsFile(settingsFile);
                if (addToSources)
                    sources.Add(new JsonConfigurationSource {
                    FileProvider = fileProvider,
                    OnLoadException = null,
                    Optional = optionalField,
                    Path = $"{settingsFile}",
                    ReloadDelay = 100,
                    ReloadOnChange = false
                });
            }
        }

        private static (bool, bool) CheckSettingsFile(string fileName)
        {
            return fileName switch {
                "testsettings.docker.json" when Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") !=
                                                null => (true, false),
                "testsettings.local.json" => (true, true),
                "testsettings.json" => (true, false),
                _ => (false, false)
            };
        }
    }
}
