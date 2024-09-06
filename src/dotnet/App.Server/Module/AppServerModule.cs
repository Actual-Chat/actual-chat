using System.IO.Compression;
using ActualChat.App.Server.Health;
using ActualChat.Db.Diagnostics;
using ActualChat.Diagnostics;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Redis.Module;
using ActualLab.CommandR.Diagnostics;
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
using ActualLab.Fusion.Diagnostics;
using ActualLab.IO;
using ActualLab.Rpc.Diagnostics;
using ActualLab.Rpc.Server;
using ActualChat.MLSearch.Diagnostics;
using ActualLab.Fusion.Server;

namespace ActualChat.App.Server.Module;

public sealed class AppServerModule(IServiceProvider moduleServices)
    : HostModule<HostSettings>(moduleServices), IWebServerModule
{
    public static readonly string AppVersion =
        typeof(AppServerModule).Assembly.GetInformationalVersion() ?? "0.0-unknown";

    private IWebHostEnvironment? _env;

    public IWebHostEnvironment Env => _env ??= ModuleServices.GetRequiredService<IWebHostEnvironment>();

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

        // Static files
        app.UseBlazorFrameworkFiles();
        app.UseDistFiles();
        // Explicit rewrite cause files without extension (hence no content-type) are not served due to security reasons
        app.UseRewriter(
            new RewriteOptions().AddRewrite("\\.well-known/apple-app-site-association$",
                ".well-known/apple-app-site-association.json",
                true));
        app.UseStaticFiles();

        // Swagger
        /*
        app.UseSwagger();
        app.UseSwaggerUI(c => {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
        });
        */

        // Response compression
        if (!Env.IsDevelopment()) // disable compression for local development and hot reload
            app.UseResponseCompression();

        // API controllers & HTTP endpoints
        app.UseRouting();
        app.UseCors("Default");
        app.UseResponseCaching();
        app.UseAuthentication();
        app.UseEndpoints(endpoints => {
            endpoints.MapAppHealth();
            // Disabled as we disabled prometheus endpoint recently
            // endpoints.MapAppMetrics();
            endpoints.MapBlazorHub();
            endpoints.MapRpcWebSocketServer();
            endpoints.MapControllers();
            endpoints.MapFallbackToPage("/_Host");
        });
        // app.UseOpenTelemetryPrometheusScrapingEndpoint();
    }

    protected override void InjectServices(IServiceCollection services)
    {
        // Host options
        services.Configure<HostOptions>(o => {
            o.ShutdownTimeout = Env.IsDevelopment()
                ? TimeSpan.FromSeconds(1)
                : TimeSpan.FromSeconds(30);
        });

        // AppHostLifecycleMonitor
        services.AddHostedService<AppHostLifecycleMonitor>();

        // Health-checks
        services.AddSingleton<LivelinessHealthCheck>(c => new LivelinessHealthCheck(c));
        services.AddSingleton<ReadinessHealthCheck>(c => new ReadinessHealthCheck(c));
        services.AddHealthChecks()
            .AddCheck<LivelinessHealthCheck>("App-Liveliness", tags: new[] { HealthTags.Live })
            .AddCheck<ReadinessHealthCheck>("App-Readiness", tags: new[] { HealthTags.Ready });

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<InfrastructureDbContext>(services);

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
            "http://localhost",
            "https://localhost",
            "app://localhost",
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
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);

        // Blazor Server
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
            o.StatefulReconnectBufferSize = 1000;
        });
        services.AddBlazorCircuitActivitySuppressor();

        // OpenTelemetry
        var openTelemetryEndpoint = Settings.OpenTelemetryEndpoint;
        if (openTelemetryEndpoint.IsNullOrEmpty())
            openTelemetryEndpoint = "localhost";

        var (host, port) = openTelemetryEndpoint.ParseHostPort(4317);
        var openTelemetryEndpointUri = $"http://{host}:{port.Format()}".ToUri();
        Log.LogInformation("OpenTelemetry endpoint: {OpenTelemetryEndpoint}", openTelemetryEndpointUri.ToString());
        services.AddOpenTelemetry()
            .ConfigureResource(builder => _ = Env.IsDevelopment()
                    ? builder.AddService("App", "actualchat", AppVersion, false, "dev")
                    : builder.AddService("App", "actualchat", AppVersion))
            .WithMetrics(builder => builder
                // gcloud exporter doesn't support some of metrics yet:
                // - https://github.com/open-telemetry/opentelemetry-collector-contrib/discussions/2948
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddMeter("Npgsql") // Npgsql meter at Npgsql.MetricsReporter
                .AddMeter(RpcInstruments.Meter.Name) // ActualLab.Rpc
                .AddMeter(CommanderInstruments.Meter.Name) // ActualLab.Commander
                .AddMeter(FusionInstruments.Meter.Name) // ActualLab.Fusion
                // Our own meters (one per assembly)
                .AddMeter(DbInstruments.Meter.Name)
                .AddMeter(CoreServerInstruments.Meter.Name)
                .AddMeter(AppInstruments.Meter.Name)
                .AddMeter(AppUIInstruments.Meter.Name)
                .AddMeter(MLSearchInstruments.Meter.Name)
                // Disabled prometheus endpoint to test Otlp
                // .AddPrometheusExporter(cfg => { // OtlpExporter doesn't work for metrics ???
                //     cfg.ScrapeEndpointPath = "/metrics";
                //     cfg.ScrapeResponseCacheDurationMilliseconds = 300;
                //     // commented out as OpenTelemetry.Exporter.Prometheus.AspNetCore 1.7.0-rc.1 doesn't support it
                //     // and 1.8.0 doesn't allow the managed Prometheus collector to collect metrics
                //     // cfg.DisableTotalNameSuffixForCounters = true;
                // })
                .AddOtlpExporter(cfg => {
                    cfg.ExportProcessorType = ExportProcessorType.Batch;
                    cfg.BatchExportProcessorOptions = new BatchExportActivityProcessorOptions {
                        ExporterTimeoutMilliseconds = 10_000,
                        MaxExportBatchSize = 200, // Google Cloud Monitoring limits batches to 200 metric points.
                        MaxQueueSize = 1024,
                        ScheduledDelayMilliseconds = 20_000,
                    };
                    cfg.Protocol = OtlpExportProtocol.Grpc;
                    cfg.Endpoint = openTelemetryEndpointUri;
                })
            )
            .WithTracing(builder => builder
                .SetErrorStatusOnException()
                .AddSource(RpcInstruments.ActivitySource.Name) // ActualLab.Rpc
                .AddSource(CommanderInstruments.ActivitySource.Name) // ActualLab.Commander
                .AddSource(FusionInstruments.ActivitySource.Name) // ActualLab.Fusion
                // Our own activity sources (one per assembly)
                .AddSource(DbInstruments.ActivitySource.Name)
                .AddSource(CoreServerInstruments.ActivitySource.Name)
                .AddSource(AppInstruments.ActivitySource.Name)
                .AddSource(AppUIInstruments.ActivitySource.Name)
                .AddSource(MLSearchInstruments.ActivitySource.Name)
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
}
