using System.Net;
using System.Reflection;
using ActualChat.Hosting;
using ActualChat.Web.Module;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
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

        // UriMapper
        services.AddSingleton(c => {
            var baseUri = Settings.BaseUri;
            if (!baseUri.IsNullOrEmpty())
                return new UriMapper(baseUri);

            var server = c.GetRequiredService<IServer>();
            var serverAddressesFeature =
                server.Features.Get<IServerAddressesFeature>() ?? throw new Exception("Can't get server address");
            baseUri = serverAddressesFeature.Addresses.First();
            return new UriMapper(baseUri);
        });

        // Plugins (IPluginHost)
        services.AddSingleton(Plugins);

        // Fusion services
        var hostName = Dns.GetHostName().ToLowerInvariant();
        services.AddSingleton(new Publisher.Options { Id = hostName });
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

        // OpenTelemetry
        var openTelemetryEndpoint = Settings.OpenTelemetryEndpoint;
        if (!openTelemetryEndpoint.IsNullOrEmpty()) {
            var (host, port) = openTelemetryEndpoint.ParseHostPort(4317);
            var openTelemetryEndpointUri = new Uri(Invariant($"http://{host}:{port}"));
            const string version = ThisAssembly.AssemblyInformationalVersion;
            services.AddOpenTelemetryMetrics(builder => builder
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("App", "actualchat", version))
                // gcloud exporter doesn't support some of metrics yet https://github.com/open-telemetry/opentelemetry-collector-contrib/discussions/2948
                // .AddAspNetCoreInstrumentation()
                .AddOtlpExporter(cfg => {
                    cfg.ExportProcessorType = OpenTelemetry.ExportProcessorType.Simple;
                    cfg.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    cfg.MetricExportIntervalMilliseconds = 5000;
                    cfg.AggregationTemporality = AggregationTemporality.Cumulative;
                    cfg.Endpoint = openTelemetryEndpointUri;
                })
            );
            services.AddOpenTelemetryTracing(builder => builder
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("App", "actualchat", version))
                .AddAspNetCoreInstrumentation(opt => {
                    var excludedPaths = new PathString[] {
                        "/favicon.ico",
                        "/metrics",
                        "/status",
                        "/_blazor",
                        "/_framework",
                    };
                    opt.Filter = httpContext =>
                        !excludedPaths.Any(x => httpContext.Request.Path.StartsWithSegments(x, StringComparison.OrdinalIgnoreCase));
                    opt.EnableGrpcAspNetCoreSupport = true;
                    opt.RecordException = true;
                })
                .AddHttpClientInstrumentation(cfg => cfg.RecordException = true)
                .AddGrpcClientInstrumentation()
                .AddNpgsql()
                .AddRedisInstrumentation()
                .AddOtlpExporter(cfg => {
                    cfg.ExportProcessorType = OpenTelemetry.ExportProcessorType.Simple;
                    cfg.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    cfg.Endpoint = openTelemetryEndpointUri;
                })
            );
        }
    }
}
