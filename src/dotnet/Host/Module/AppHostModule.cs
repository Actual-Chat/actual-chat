using System.Reflection;
using ActualChat.Hosting;
using ActualChat.Web.Module;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;
using Stl.DependencyInjection;
using Stl.Fusion.Blazor;
using Stl.Fusion.Bridge;
using Stl.Fusion.Client;
using Stl.Fusion.Server;
using Stl.Plugins;

namespace ActualChat.Host.Module;

public class AppHostModule : HostModule<HostSettings>, IWebModule
{
    public IWebHostEnvironment Env { get; } = null!;
    public IConfiguration Cfg { get; } = null!;

    public AppHostModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public AppHostModule(IPluginHost plugins) : base(plugins)
    {
        Env = Plugins.GetRequiredService<IWebHostEnvironment>();
        Cfg = Plugins.GetRequiredService<IConfiguration>();
    }

    public void ConfigureApp(IApplicationBuilder app)
    {
        // This server serves static content from Blazor Client,
        // and since we don't copy it to local wwwroot,
        // we need to find Client's wwwroot in bin/(Debug/Release) folder
        // and set it as this server's content root.
        Env.WebRootPath = AppPathResolver.GetWebRootPath();
        Env.ContentRootPath = AppPathResolver.GetContentRootPath();
        Env.WebRootFileProvider = new PhysicalFileProvider(Env.WebRootPath);
        Env.ContentRootFileProvider = new PhysicalFileProvider(Env.ContentRootPath);
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

        app.UseWebSockets(new WebSocketOptions {
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
        app.UseResponseCaching();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints => {
            endpoints.MapBlazorHub();
            endpoints.MapFusionWebSocketServer();
            endpoints.MapControllers();
            endpoints.MapFallbackToPage("/_Host");
        });
    }

    public override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        // Plugins (IPluginHost)
        services.AddSingleton(Plugins);

        // Fusion services
        services.AddSingleton(new Publisher.Options { Id = Settings.PublisherId });
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
            c.SwaggerDoc("v1",
                new OpenApiInfo {
                    Title = "ActualChat API", Version = "v1",
                });
        });

        // UriMapper
        services.AddSingleton(c => {
            var publicUrl = Settings.PublicUrl;
            if (!publicUrl.IsNullOrEmpty())
                return new UriMapper(new Uri(publicUrl));

            var server = c.GetRequiredService<IServer>();
            var serverAddressesFeature =
                server.Features.Get<IServerAddressesFeature>() ?? throw new Exception("Can't get server address");
            var baseUri = new Uri(serverAddressesFeature.Addresses.First());
            return new UriMapper(baseUri);
        });
    }
}
