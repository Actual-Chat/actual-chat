using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ActualChat.Host;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Server.Authentication;
using Stl.IO;
using Stl.Testing;

namespace ActualChat.Testing
{
    public static class TestHostFactory
    {
        public static async Task<AppHost> NewAppHost(Action<AppHost>? configure = null)
        {
            var port = GetUnusedTcpPort();
            var manifestPath = GetManifestPath();
            var appHost = new AppHost() {
                ServerUrls = WebTestEx.GetLocalUri(port).ToString(),
                HostConfigurationBuilder = cfg => {
                    cfg.Sources.Insert(0, new MemoryConfigurationSource() {
                        InitialData = new Dictionary<string, string>() {
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

        private static int GetUnusedTcpPort()
        {
            var listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
            try {
                return ((IPEndPoint) listener.LocalEndpoint).Port;
            }
            finally {
                listener.Stop();
            }
        }

        public static PathString GetManifestPath()
        {
            PathString AssemblyPathToManifestPath(PathString assemblyPath)
                => assemblyPath.ChangeExtension("StaticWebAssets.xml");

            var hostAssemblyPath = (PathString) typeof(AppHost).Assembly.Location;
            var hostAssemblyFileName = hostAssemblyPath.FileName;
            var manifestPath = AssemblyPathToManifestPath(hostAssemblyPath);
            if (File.Exists(manifestPath))
                return manifestPath;

            var baseDir = hostAssemblyPath.DirectoryPath;
            var binCfgPart = Regex.Match(baseDir.Value, @"[\\/]bin[\\/]\w+[\\/]").Value;
            var relativePath = $"../../src/Host/{binCfgPart}/net5.0/" & hostAssemblyFileName;
            for (var i = 0; i < 4; i++) {
                manifestPath = AssemblyPathToManifestPath(baseDir & relativePath);
                if (File.Exists(manifestPath))
                    return manifestPath;
                relativePath = "../" + relativePath;
            }
            throw new ApplicationException("Can't find manifest.");
        }
    }
}
