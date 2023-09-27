using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Commands;
using ActualChat.Contacts;
using ActualChat.Feedback;
using ActualChat.Hosting;
using ActualChat.Invite;
using ActualChat.Kubernetes;
using ActualChat.Notification;
using ActualChat.Transcription;
using ActualChat.Users;
using ActualChat.Web.Module;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Stl.Diagnostics;
using Stl.Fusion.EntityFramework;
using Stl.IO;
using Stl.Rpc.Server;

namespace ActualChat.App.Server.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ServerAppModule : HostModule<HostSettings>, IWebModule
{
    public static string AppVersion { get; } =
        typeof(ServerAppModule).Assembly.GetInformationalVersion() ?? "0.0-unknown";

    private IWebHostEnvironment? _env;

    public IWebHostEnvironment Env => _env ??= ModuleServices.GetRequiredService<IWebHostEnvironment>();

    public ServerAppModule(IServiceProvider moduleServices) : base(moduleServices) { }

    public void ConfigureApp(IApplicationBuilder app)
    {
        if (Settings.AssumeHttps) {
            Log.LogWarning("AssumeHttps is on");
            app.Use((context, next) => {
                context.Request.Scheme = "https";
                return next();
            });
        }

        // This server serves static content from Blazor Client,
        // and since we don't copy it to local wwwroot,
        // we need to find Client's wwwroot in bin/(Debug/Release) folder
        // and set it as this server's content root.
        Env.WebRootPath =  Settings.WebRootPath.NullIfEmpty() ?? AppPathResolver.GetWebRootPath();
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
        var baseUrl = Settings.BaseUri;
        if (!baseUrl.IsNullOrEmpty()) {
            Log.LogInformation("Overriding request host to {BaseUrl}", baseUrl);
            app.UseBaseUrl(baseUrl);
        }

        app.UseWebSockets(new WebSocketOptions {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });

        // Static + Swagger
        app.UseBlazorFrameworkFiles();
        app.UseDistFiles();
        // Explicit rewrite cause files without extension (hence no content-type) are not served due to security reasons
        app.UseRewriter(
            new RewriteOptions().AddRewrite("\\.well-known/apple-app-site-association$",
                ".well-known/apple-app-site-association.json",
                true));
        app.UseStaticFiles();

        /*
        app.UseSwagger();
        app.UseSwaggerUI(c => {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
        });
        */

        // Response compression
        app.UseResponseCompression();

        // API controllers
        app.UseRouting();
        app.UseCors("Default");
        app.UseResponseCaching();
        app.UseAuthentication();
        app.UseEndpoints(endpoints => {
            endpoints.MapAppHealth();
            endpoints.MapAppMetrics();
            endpoints.MapBlazorHub();
            endpoints.MapRpcWebSocketServer();
            endpoints.MapControllers();
            endpoints.MapFallbackToPage("/_Host");
        });
        app.UseOpenTelemetryPrometheusScrapingEndpoint();
    }

    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.AppKind.IsServer())
            return; // Server-side only module

        // Host options
        services.Configure<HostOptions>(o => {
            o.ShutdownTimeout = Env.IsDevelopment()
                ? TimeSpan.FromSeconds(1)
                : TimeSpan.FromSeconds(30);
        });

        // Queues
        services.AddLocalCommandQueues();
        services.AddCommandQueueScheduler();

        // Web
        var binPath = new FilePath(Assembly.GetExecutingAssembly().Location).FullPath.DirectoryPath;
        var dataProtection = Settings.DataProtection.NullIfEmpty() ?? binPath & "data-protection-keys";
        Log.LogInformation("DataProtection path: {DataProtection}", dataProtection);
        if (dataProtection.OrdinalStartsWith("gs://")) {
            var bucket = dataProtection[5..dataProtection.IndexOf('/', 5)];
            var objectName = dataProtection[(6 + bucket.Length)..];
            services.AddDataProtection().PersistKeysToGoogleCloudStorage(bucket, objectName);
        }
        else {
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtection));
        }
        // TODO: setup security headers: better CSP, Referrer-Policy / X-Content-Type-Options / X-Frame-Options etc
        services.AddCors(options => {
            options.AddPolicy("Default", builder => {
                builder.AllowAnyOrigin();
                builder.WithOrigins(
                        "http://0.0.0.0",
                        "https://0.0.0.0",
                        "app://0.0.0.0",
                        "http://0.0.0.0:7080",
                        "https://0.0.0.0:7080",
                        "https://0.0.0.0:7081",
                        "https://cdpn.io"
                    )
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });
        /*
        services.Configure<HstsOptions>(options => {
            options.ExcludedHosts.Add("local.actual.chat");
            options.ExcludedHosts.Add("localhost");
        });
        */
        services.Configure<ForwardedHeadersOptions>(options => {
            options.ForwardedHeaders = ForwardedHeaders.All;
            if (Settings.AssumeHttps)
                options.ForwardedHeaders &= ~ForwardedHeaders.XForwardedProto;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        // Compression
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        services.AddResponseCompression(o => {
            o.EnableForHttps = true;
            o.Providers.Add<BrotliCompressionProvider>();
        });

        services.AddRouting();
        services.AddMvc().AddApplicationPart(Assembly.GetExecutingAssembly());
        services.AddServerSideBlazor(o => {
            o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(2); // Default is 3 min.
            o.MaxBufferedUnacknowledgedRenderBatches = 1000; // Default is 10
            o.DetailedErrors = true;
        }).AddHubOptions(o => {
            o.MaximumParallelInvocationsPerClient = 4;
        });

        // OpenTelemetry
        services.AddSingleton<OtelMetrics>();
        var otelBuilder = services.AddOpenTelemetry()
            .WithMetrics(builder => builder
                // gcloud exporter doesn't support some of metrics yet:
                // - https://github.com/open-telemetry/opentelemetry-collector-contrib/discussions/2948
                .AddAspNetCoreInstrumentation()
                .AddMeter(AppMeter.Name)
                .AddMeter(typeof(IComputed).GetMeter().Name) // Fusion meter
                .AddMeter(typeof(ICommand).GetMeter().Name) // Commander meters
                .AddMeter(MeterExt.Unknown.Name) // Unknown meter
                .AddPrometheusExporter(cfg => { // OtlpExporter doesn't work for metrics ???
                    cfg.ScrapeEndpointPath = "/metrics";
                    cfg.ScrapeResponseCacheDurationMilliseconds = 300;
                })
            );
        var openTelemetryEndpoint = Settings.OpenTelemetryEndpoint;
        if (!openTelemetryEndpoint.IsNullOrEmpty()) {
            var (host, port) = openTelemetryEndpoint.ParseHostPort(4317);
            var openTelemetryEndpointUri = $"http://{host}:{port.Format()}".ToUri();
            Log.LogInformation("OpenTelemetry endpoint: {OpenTelemetryEndpoint}", openTelemetryEndpointUri.ToString());
            otelBuilder = otelBuilder.WithTracing(builder => builder
                .SetErrorStatusOnException()
                .AddSource(AppTrace.Name)
                .AddSource(typeof(IComputed).GetActivitySource().Name) // Fusion trace
                .AddSource(typeof(ICommand).GetActivitySource().Name) // Commander trace
                .AddSource(typeof(IAuthBackend).GetActivitySource().Name) // DB Session Info trim
                .AddSource(typeof(DbKey).GetActivitySource().Name) // DB Entity resolver
                .AddSource(typeof(IAudioStreamer).GetActivitySource().Name)
                .AddSource(typeof(IChats).GetActivitySource().Name)
                .AddSource(typeof(IContacts).GetActivitySource().Name)
                .AddSource(typeof(IFeedbacks).GetActivitySource().Name)
                .AddSource(typeof(IInvites).GetActivitySource().Name)
                .AddSource(typeof(ITranscriber).GetActivitySource().Name)
                .AddSource(typeof(INotifications).GetActivitySource().Name)
                .AddSource(typeof(IAccounts).GetActivitySource().Name)
                .AddSource(typeof(Constants).GetActivitySource().Name)
                .AddSource(typeof(KubeServices).GetActivitySource().Name)
                .AddSource(ActivitySourceExt.Unknown.Name) // Unknown meter
                .AddAspNetCoreInstrumentation(opt => {
                    var excludedPaths = new PathString[] {
                        "/favicon.ico",
                        "/metrics",
                        "/status",
                        "/_blazor",
                        "/_framework",
                        "/healthz",
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
                        MaxExportBatchSize = 200, // Google Cloud Monitoring limits batches to 200 metric points.
                        MaxQueueSize = 1024,
                        ScheduledDelayMilliseconds = 20_000,
                    };
                    cfg.Protocol = OtlpExportProtocol.Grpc;
                    cfg.Endpoint = openTelemetryEndpointUri;
                })
            );
        }
        otelBuilder.ConfigureResource(builder => builder
            .AddService("App", "actualchat", AppVersion));
    }
}
