using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ActualChat.Streaming.Module;
using ActualChat.Host.Internal;
using ActualChat.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.Blazor;
using Stl.Fusion.Bridge;
using Stl.Fusion.Client;
using Stl.Fusion.Server;
using Stl.IO;
using Stl.Plugins;

namespace ActualChat.Host.Module
{
    public interface IWebHostModule
    {
        void ConfigureApp(IApplicationBuilder app, IHubRegistrar hubRegistrar);
    }

    public class WebHostModule : HostModule<HostSettings>, IWebHostModule
    {
        public IWebHostEnvironment Env { get; } = null!;
        public IConfiguration Cfg { get; } = null!;

        public WebHostModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public WebHostModule(IPluginHost plugins) : base(plugins)
        {
            Env = Plugins.GetRequiredService<IWebHostEnvironment>();
            Cfg = Plugins.GetRequiredService<IConfiguration>();
        }

        public override void InjectServices(IServiceCollection services)
        {
            base.InjectServices(services);
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
                return; // Server-side only module

            // Plugins (IPluginHost)
            services.AddSingleton(Plugins);
            services.AddSingleton<HostPluginHelper>();

            // Fusion services
            services.AddSingleton(new Publisher.Options() { Id = Settings.PublisherId });
            var fusion = services.AddFusion();
            var fusionServer = fusion.AddWebServer();
            var fusionClient = fusion.AddRestEaseClient();
            var fusionAuth = fusion.AddAuthentication();

            // Web
            services.AddRouting();
            services.AddMvc().AddApplicationPart(Assembly.GetExecutingAssembly());
            services.AddServerSideBlazor(o => {
                o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(15);
                o.DetailedErrors = true;
            });
            fusionAuth.AddBlazor(_ => { }); // Must follow services.AddServerSideBlazor()!

            // Swagger & debug tools
            services.AddSwaggerGen(c => {
                c.SwaggerDoc("v1", new OpenApiInfo {
                    Title = "ActualChat API", Version = "v1"
                });
            });

            // UriMapper
            services.AddSingleton(c => {
                var server = c.GetRequiredService<IServer>();
                var serverAddressesFeature = server.Features.Get<IServerAddressesFeature>();
                var baseUri = new Uri(serverAddressesFeature.Addresses.First());
                return new UriMapper(baseUri);
            });
        }

        public virtual void ConfigureApp(IApplicationBuilder app, IHubRegistrar hubRegistrar)
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
