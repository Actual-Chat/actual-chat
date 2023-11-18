using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using ActualChat.App.Server.Health;
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
using ActualChat.Web.Internal;
using ActualChat.Web.Module;
using ActualChat.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Stl.Diagnostics;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.Server;
using Stl.Fusion.Server.Middlewares;
using Stl.Fusion.Server.Rpc;
using Stl.IO;
using Stl.Rpc;
using Stl.Rpc.Diagnostics;
using Stl.Rpc.Server;

namespace ActualChat.App.Server.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class AppServerModule : HostModule<HostSettings>, IWebModule
{
    public static readonly string AppVersion =
        typeof(AppServerModule).Assembly.GetInformationalVersion() ?? "0.0-unknown";

    private IWebHostEnvironment? _env;

    public IWebHostEnvironment Env => _env ??= ModuleServices.GetRequiredService<IWebHostEnvironment>();

    public AppServerModule(IServiceProvider moduleServices) : base(moduleServices) { }

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
        if (!Env.IsDevelopment()) // disable compression for local development and hot reload
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

        // Health-checks
        services.AddSingleton<LivelinessHealthCheck>(c => new LivelinessHealthCheck(c));
        services.AddSingleton<ReadinessHealthCheck>(c => new ReadinessHealthCheck(c));
        services.AddHealthChecks()
            .AddCheck<LivelinessHealthCheck>("App-Liveliness", tags: new[] { HealthTags.Live })
            .AddCheck<ReadinessHealthCheck>("App-Readiness", tags: new[] { HealthTags.Ready });

        // Queues
        services.AddLocalCommandQueues();
        services.AddCommandQueueScheduler();

        // Fusion server + Rpc configuration
        var fusion = services.AddFusion();
        var rpc = fusion.Rpc;
        fusion.AddWebServer();

        // Remove SessionMiddleware - we use SessionCookies directly instead
        services.RemoveAll<SessionMiddleware.Options>();
        services.RemoveAll<SessionMiddleware>();
        // Replace RpcServerConnectionFactory with AppRpcConnectionFactory
        services.AddSingleton(_ => new AppRpcServerConnectionFactory());
        services.AddSingleton<RpcServerConnectionFactory>(c => c.GetRequiredService<AppRpcServerConnectionFactory>().Invoke);
        // Replace DefaultSessionReplacerRpcMiddleware with AppDefaultSessionReplacerRpcMiddleware
        rpc.RemoveInboundMiddleware<DefaultSessionReplacerRpcMiddleware>();
        rpc.AddInboundMiddleware<AppDefaultSessionReplacerRpcMiddleware>();

        // Add RpcMethodActivityTracer
        services.AddSingleton<RpcMethodTracerFactory>(method => new RpcMethodActivityTracer(method) {
            UseCounters = true,
        });

        // Debug: add RpcRandomDelayMiddleware if you'd like to check how it works w/ delays
#if DEBUG && false
        rpc.AddInboundMiddleware(c => new RpcRandomDelayMiddleware(c) {
            Delay = new(0.2, 0.2), // 0 .. 0.4s
        });
#endif

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
        var origins = new List<string> {
            "http://0.0.0.0",
            "https://0.0.0.0",
            "app://0.0.0.0",
        };
        if (Env.IsDevelopment()) {
            origins.Add("https://local.actual.chat");
            origins.Add("https://dev.actual.chat");
        }
        else if (Env.IsStaging()) {
            origins.Add("https://dev.actual.chat");
            origins.Add("https://stg.actual.chat");
        }
        else
            origins.Add("https://actual.chat");

        services.AddCors(options => {
            options.AddPolicy("Default", builder => {
                builder.WithOrigins(origins.ToArray())
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
            options.AddPolicy("CDN", builder => {
                builder
                    .WithOrigins(origins.ToArray())
                    .AllowAnyOrigin()
                    .WithMethods("GET")
                    .AllowAnyHeader()
                    .WithExposedHeaders("Content-Encoding","Content-Length","Content-Range", "Content-Type");
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
        if (!Env.IsDevelopment()) { // Disable compression for local development and hot reload
            services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
            services.AddResponseCompression(o => {
                o.EnableForHttps = true;
                o.Providers.Add<BrotliCompressionProvider>();
            });
        }

        // Controllers, etc.
        services.AddRouting();
#pragma warning disable IL2026
        var mvc = services.AddMvc(options => {
            options.ModelBinderProviders.Add(new ModelBinderProvider());
            options.ModelMetadataDetailsProviders.Add(new ValidationMetadataProvider());
        });
        mvc.AddApplicationPart(Assembly.GetExecutingAssembly());
        services.AddServerSideBlazor(o => {
            if (HostInfo.IsDevelopmentInstance) {
                o.DisconnectedCircuitMaxRetained = 5;
                o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(1);
            }
            else { // Production
                o.DisconnectedCircuitMaxRetained = 100; // Default is 100
                o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(2); // Default is 3 min.
            }
            o.MaxBufferedUnacknowledgedRenderBatches = 1000; // Default is 10
            o.DetailedErrors = true;
        }).AddHubOptions(o => {
            o.MaximumParallelInvocationsPerClient = 4;
        });
#pragma warning restore IL2026

        // OpenTelemetry
        services.AddSingleton<OtelMetrics>();
        var otelBuilder = services.AddOpenTelemetry()
            .WithMetrics(builder => builder
                // gcloud exporter doesn't support some of metrics yet:
                // - https://github.com/open-telemetry/opentelemetry-collector-contrib/discussions/2948
                .AddAspNetCoreInstrumentation()
                .AddMeter(typeof(RpcHub).GetMeter().Name) // Stl.Rpc
                .AddMeter(typeof(ICommand).GetMeter().Name) // Stl.Commander
                .AddMeter(typeof(IComputed).GetMeter().Name) // Stl.Fusion
                // Our own meters (one per assembly)
                .AddMeter(AppMeter.Name)
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
                .AddSource(typeof(RpcHub).GetActivitySource().Name) // Stl.Rpc
                .AddSource(typeof(ICommand).GetActivitySource().Name) // Stl.Commander
                .AddSource(typeof(IComputed).GetActivitySource().Name) // Stl.Fusion
                .AddSource(typeof(IAuthBackend).GetActivitySource().Name) // Stl.Fusion.Ext.Services - auth, etc.
                .AddSource(typeof(DbKey).GetActivitySource().Name) // Stl.Fusion.EntityFramework
                // Our own activity sources (one per assembly)
                .AddSource(AppTrace.Name)
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
