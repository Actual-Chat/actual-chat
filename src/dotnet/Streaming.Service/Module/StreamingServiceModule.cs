using ActualChat.Audio;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Redis.Module;
using ActualChat.Streaming.Services;
using ActualChat.Streaming.Services.Transcribers;
using Microsoft.AspNetCore.Builder;
using GoogleTranscriber = ActualChat.Streaming.Services.Transcribers.GoogleTranscriber;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming.Module;

public sealed class StreamingServiceModule(IServiceProvider moduleServices)
    : HostModule<StreamingSettings>(moduleServices), IWebServerModule
{
    public void ConfigureApp(IApplicationBuilder app)
    {
        if (HostInfo.HasRole(HostRole.Api)) {
            // SignalR hub endpoints
            app.UseEndpoints(endpoints => {
                endpoints.MapHub<StreamHub>("/api/hub/streams", options => options.AllowStatefulReconnects = true);
                endpoints.MapHub<StreamHub>("/api/hub/audio"); // For backward compatibility!
            });
        }
    }

    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IStreamingBackend>().IsClient();

        // SignalR hub
        if (rpcHost.IsApiHost) {
            var signalR = services.AddSignalR(options => {
                options.StreamBufferCapacity = 20;
                options.EnableDetailedErrors = false;
                options.StatefulReconnectBufferSize = 2000;
            });
            signalR.AddJsonProtocol();
            signalR.AddMessagePackProtocol();
            services.AddScoped<StreamHub>();
        }

        rpcHost.AddApi<IStreamServer, StreamServer>();
        rpcHost.AddBackend<IStreamingBackend, StreamingBackend>();
        services.AddSingleton<IStreamClient, StreamBackendClient>(); // Client for IStreamingBackend
        services.AddSingleton<AudioDownloader, BlobStorageAudioDownloader>(); // Server-side AudioDownloader
        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Internal services
        services.AddSingleton<ITranscriberFactory, TranscriberFactory>();
        services.AddSingleton<GoogleTranscriber>();
        services.AddSingleton<DeepgramTranscriber>();
        services.AddSingleton<AudioSegmentSaver>();
        services.AddSingleton<StreamingBackend.Options>();

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<StreamingContext>(services);
    }
}
