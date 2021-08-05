using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ActualChat.Host;
using ActualChat.Hosting;

var host = Host.CreateDefaultBuilder()
    .ConfigureHostConfiguration(builder => {
        // Looks like there is no better way to set _default_ URL
        builder.Sources.Insert(0, new MemoryConfigurationSource() {
            InitialData = new Dictionary<string, string>() {
                {WebHostDefaults.ServerUrlsKey, "http://localhost:7080"},
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
        .UseStartup<Startup>())
    .Build();

var dbInitializers = host.Services.GetServices<IDataInitializer>();
var initTasks = dbInitializers.Select(i => i.Initialize(true)).ToArray();
await Task.WhenAll(initTasks);

await host.RunAsync();
