using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ActualChat.Host;
using ActualChat.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stl.Fusion;
using Stl.Fusion.Client;

namespace Tests.UI.Blazor
{
    public class TestHost
    {
        public static IHost MainTestHost { get; set; }
        public static async Task CreateBasicHost()
        {
            MainTestHost = Host.CreateDefaultBuilder()
                .ConfigureHostConfiguration(builder => {
                    builder.Sources.Insert(0, new MemoryConfigurationSource() {
                        InitialData = new Dictionary<string, string>() {
                            {WebHostDefaults.ServerUrlsKey, "http://localhost:7081"},
                            {"Host:IsTestServer", true.ToString()}
                        }
                    });
                })
                .ConfigureWebHostDefaults(builder => builder
                    .UseDefaultServiceProvider((ctx, options) => {
                        if (ctx.HostingEnvironment.IsDevelopment()) {
                            options.ValidateScopes = true;
                            options.ValidateOnBuild = true;
                        }
                    })
                    .UseStartup<TestStartup>())
                .Build();

            var dbInitializers = MainTestHost.Services.GetServices<IDataInitializer>();
            var initTasks = dbInitializers.Select(i => i.Initialize(true)).ToArray();
            
            await Task.WhenAll(initTasks);
            MainTestHost.RunAsync();
            await Task.Delay(500);
        }
    }
}
