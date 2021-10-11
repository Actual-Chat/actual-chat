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
using Stl.Testing;

namespace ActualChat.Testing.Host
{
    public static class TestHostFactory
    {
        private static readonly List<string> TestSettingsFiles = new List<string>()
            { "testsettings.json", "testsettings.docker.json", "testsettings.local.json" };

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
            var sources = config.Sources;
            var jsonSettings = sources.FirstOrDefault(
                                   x => x is JsonConfigurationSource b && b.Path == "appsettings.json") ??
                               throw new Exception("Can't find appsettings.json source.");
            var jsonDevSettings = sources.FirstOrDefault(
                                      x => x is JsonConfigurationSource b &&
                                           b.Path == "appsettings.Development.json") ??
                                  throw new Exception("Can't find appsettings.Development.json source.");
            sources.Remove(jsonSettings);
            sources.Remove(jsonDevSettings);
            foreach (var settingFile in TestSettingsFiles) {
                sources.Add(new JsonConfigurationSource {
                    FileProvider =
                        new PhysicalFileProvider(Path.GetDirectoryName(typeof(TestSettings).Assembly.Location)),
                    OnLoadException = null,
                    Optional = false,
                    Path = $"{settingFile}",
                    ReloadDelay = 100,
                    ReloadOnChange = false
                });
            }
        }
    }
}
