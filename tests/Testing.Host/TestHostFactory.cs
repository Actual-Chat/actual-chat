using ActualChat.Host;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Server.Authentication;
using Stl.IO;
using Stl.Testing;

namespace ActualChat.Testing.Host
{
    public static class TestHostFactory
    {
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
                }
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
    }
}
