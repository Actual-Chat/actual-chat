using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ActualChat.Distribution.Module;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi.Models;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.Blazor;
using Stl.Fusion.Bridge;
using Stl.Fusion.Client;
using Stl.Fusion.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Collections;
using Stl.Plugins;
using Stl.Text;
using PathString = Stl.IO.PathString;

namespace ActualChat.Host
{
    public class Startup
    {
        private IConfiguration Cfg { get; }
        private IWebHostEnvironment Env { get; }
        private IPluginHost Plugins { get; set; } = null!;
        private HostSettings HostSettings => Plugins.GetRequiredService<HostSettings>();
        private ILogger Log => Plugins?.GetService<ILogger<Startup>>() ?? NullLogger<Startup>.Instance;

        public Startup(IConfiguration cfg, IWebHostEnvironment environment)
        {
            Cfg = cfg;
            Env = environment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // Logging
            services.AddLogging(logging => {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Warning);
                if (Env.IsDevelopment()) {
                    logging.AddFilter(typeof(App).Namespace, LogLevel.Information);
                    logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Information);
                    // logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Transaction", LogLevel.Debug);
                    logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
                    logging.AddFilter("Stl.Fusion.Operations", LogLevel.Information);
                }
            });

            // Other services shared together with plugins
            services.AddSingleton(new HostInfo() {
                HostKind = HostKind.WebServer,
                RequiredServiceScopes = ImmutableHashSet<Symbol>.Empty
                    .Add(ServiceScope.Server)
                    .Add(ServiceScope.BlazorUI),
                Environment = Env.EnvironmentName,
                Configuration = Cfg,
            });
            services.AddSettings<HostSettings>();

            // Creating plugins
            Plugins = new PluginHostBuilder(new ServiceCollection().Add(services)).Build();
            services.AddSingleton(Plugins);

            // Fusion services
            services.AddSingleton(new Publisher.Options() { Id = HostSettings.PublisherId });
            var fusion = services.AddFusion();
            var fusionServer = fusion.AddWebServer();
            var fusionClient = fusion.AddRestEaseClient();
            var fusionAuth = fusion.AddAuthentication();

            // Web
            services.AddRouting();
            services.AddMvc().AddApplicationPart(Assembly.GetExecutingAssembly());
            services.AddServerSideBlazor(o => o.DetailedErrors = true);
            fusionAuth.AddBlazor(o => { }); // Must follow services.AddServerSideBlazor()!

            // Swagger & debug tools
            services.AddSwaggerGen(c => {
                c.SwaggerDoc("v1", new OpenApiInfo {
                    Title = "ActualChat API", Version = "v1"
                });
            });

            services.AddTransient<IHostUriProvider, HostUriProvider>();

            // Injecting plugin services
            Plugins.GetPlugins<HostModule>().Apply(m => m.InjectServices(services));
        }

        public void Configure(IApplicationBuilder app, IHubRegistrar hubRegistrar)
        {
            // This server serves static content from Blazor Client,
            // and since we don't copy it to local wwwroot,
            // we need to find Client's wwwroot in bin/(Debug/Release) folder
            // and set it as this server's content root.
            var baseDir = (PathString) (Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "");
            var binCfgPart = Regex.Match(baseDir.Value, @"[\\/]bin[\\/]\w+[\\/]").Value;
            var wwwRootPath = baseDir & "wwwroot";
            if (!Directory.Exists(Path.Combine(wwwRootPath, "_framework"))) {
                // This is a regular build, not a build produced w/ "publish",
                // so we remap wwwroot to the client's wwwroot folder
                var relativeWwwRootPath = $"../../src/UI.Blazor.Host/{binCfgPart}/net5.0/wwwroot";
                for (var i = 0; i < 4; i++) {
                    wwwRootPath = baseDir & relativeWwwRootPath;
                    if (Directory.Exists(wwwRootPath))
                        break;
                    relativeWwwRootPath = "../" + relativeWwwRootPath;

                }
                if (!Directory.Exists(wwwRootPath))
                    throw new ApplicationException("Can't find 'wwwroot' folder.");
            }
            Env.WebRootPath = wwwRootPath;
            Env.WebRootFileProvider = new PhysicalFileProvider(Env.WebRootPath);
            StaticWebAssetsLoader.UseStaticWebAssets(Env, Cfg);

            if (Env.IsDevelopment()) {
                app.UseDeveloperExceptionPage();
                app.UseWebAssemblyDebugging();
            }
            else {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }
            app.UseHttpsRedirection();

            app.UseWebSockets(new WebSocketOptions() {
                KeepAliveInterval = TimeSpan.FromSeconds(30),
            });
            app.UseFusionSession();

            // Static + Swagger
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();
            app.UseSwagger();
            app.UseSwaggerUI(c => {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
            });
            
            // API controllers
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints => {
                endpoints.MapBlazorHub();
                endpoints.MapFusionWebSocketServer();
                endpoints.MapControllers();
                endpoints.MapHubs(hubRegistrar);
                endpoints.MapFallbackToPage("/_Host");
            });
        }
    }
}
