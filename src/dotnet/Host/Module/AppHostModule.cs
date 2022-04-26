using System.Net;
using System.Reflection;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Host.Authorization;
using ActualChat.Web.Module;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Stl.Diagnostics;
using Stl.Fusion.Blazor;
using Stl.Fusion.Bridge;
using Stl.Fusion.Client;
using Stl.Fusion.Server;
using Stl.Plugins;

namespace ActualChat.Host.Module;

public class AppHostModule : HostModule<HostSettings>, IWebModule
{
    public static string AppVersion { get; } =
        typeof(AppHostModule).Assembly.GetInformationalVersion() ?? "0.0-unknown";

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
 #pragma warning disable IL2026
        Env.WebRootPath =  Cfg.GetValue<string?>("Hosting:WebRootPath").NullIfEmpty() ?? AppPathResolver.GetWebRootPath();
 #pragma warning restore IL2026
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

        // See
        // - https://docs.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins?view=aspnetcore-6.0
        // - https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-6.0
        app.UseForwardedHeaders();
        // And here we can modify httpContext.Request.Scheme & Host manually to whatever we like
        if (!Settings.BaseUri.IsNullOrEmpty()) {
            var baseUri = new Uri(Settings.BaseUri);
            Log.LogInformation("Overriding request host to {BaseUri}", baseUri);
            app.UseBaseUri(baseUri);
        }

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
        app.UseCors("Default");
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
        var dataProtection = Settings.DataProtection.IsNullOrEmpty()
            ? Path.Combine(Path.GetFullPath(Path.GetDirectoryName(Assembly.GetExecutingAssembly()!.Location)!), "data-protection-keys")
            : Settings.DataProtection;
        Log.LogInformation("DataProtection path: {DataProtection}", dataProtection);
        if (dataProtection.StartsWith("gs://", StringComparison.OrdinalIgnoreCase)) {
            var bucket = dataProtection[5..dataProtection.IndexOf('/', 5)];
            var objectName = dataProtection[(6 + bucket.Length)..];
            services.AddDataProtection().PersistKeysToGoogleCloudStorage(bucket, objectName);
        }
        else {
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtection));
        }
        // TODO: setup security headers: better CSP, Referrer-Policy / X-Content-Type-Options / X-Frame-Options etc
        services.AddCors(options => {
            options.AddPolicy("Default", builder => builder.AllowAnyOrigin().WithFusionHeaders());
        });
        services.Configure<ForwardedHeadersOptions>(options => {
            options.ForwardedHeaders = ForwardedHeaders.All;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });
        services.AddRouting();
        services.AddMvc().AddApplicationPart(Assembly.GetExecutingAssembly());
        services.AddServerSideBlazor(o => {
            o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(15);
            o.DetailedErrors = true;
        });
        fusionAuth.AddBlazor(o => o.AddAppPolicies()); // Must follow services.AddServerSideBlazor()!

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
            Log.LogInformation("OpenTelemetry endpoint: {OpenTelemetryEndpoint}", openTelemetryEndpointUri.ToString());
            services.AddOpenTelemetryMetrics(builder => builder
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("App", "actualchat", AppVersion))
                // gcloud exporter doesn't support some of metrics yet:
                // - https://github.com/open-telemetry/opentelemetry-collector-contrib/discussions/2948
                .AddAspNetCoreInstrumentation()
                .AddMeter(AppMeter.Name)
                .AddMeter(typeof(IComputed).GetMeter().Name) // Fusion meter
                .AddMeter(typeof(ICommand).GetMeter().Name) // Commander meters
                .AddMeter(MeterExt.Unknown.Name) // Unknown meter
                .AddOtlpExporter(cfg => {
                    cfg.ExportProcessorType = ExportProcessorType.Batch;
                    cfg.BatchExportProcessorOptions = new BatchExportActivityProcessorOptions() {
                        ExporterTimeoutMilliseconds = 10_000,
                        MaxExportBatchSize = 256,
                        MaxQueueSize = 1024,
                        ScheduledDelayMilliseconds = 20_000,
                    };
                    cfg.Protocol = OtlpExportProtocol.Grpc;
                    cfg.Endpoint = openTelemetryEndpointUri;
                })
            );
            services.AddOpenTelemetryTracing(builder => builder
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("App", "actualchat", AppVersion))
                .SetErrorStatusOnException()
                .AddSource(AppTrace.Name)
                .AddSource(typeof(IComputed).GetActivitySource().Name) // Fusion trace
                .AddSource(typeof(ICommand).GetActivitySource().Name) // Commander trace
                .AddSource(ActivitySourceExt.Unknown.Name) // Unknown meter
                .AddAspNetCoreInstrumentation(opt => {
                    var excludedPaths = new PathString[] {
                        "/favicon.ico",
                        "/metrics",
                        "/status",
                        "/_blazor",
                        "/_framework",
                    };
                    opt.Filter = httpContext =>
                        !excludedPaths.Any(x
                            => httpContext.Request.Path.StartsWithSegments(x, StringComparison.OrdinalIgnoreCase));
                })
                .AddHttpClientInstrumentation(cfg => cfg.RecordException = true)
                .AddGrpcClientInstrumentation()
                .AddNpgsql()
                .AddOtlpExporter(cfg => {
                    cfg.ExportProcessorType = ExportProcessorType.Batch;
                    cfg.BatchExportProcessorOptions = new BatchExportActivityProcessorOptions() {
                        ExporterTimeoutMilliseconds = 10_000,
                        MaxExportBatchSize = 256,
                        MaxQueueSize = 1024,
                        ScheduledDelayMilliseconds = 20_000,
                    };
                    cfg.Protocol = OtlpExportProtocol.Grpc;
                    cfg.Endpoint = openTelemetryEndpointUri;
                })
            );
        }
    }
}
