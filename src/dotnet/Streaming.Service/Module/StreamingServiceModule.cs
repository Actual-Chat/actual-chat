using ActualChat.Audio;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Redis.Module;
using ActualChat.Live;
using ActualChat.Streaming.Services;
using ActualChat.Streaming.Services.Transcribers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GoogleTranscriber = ActualChat.Streaming.Services.Transcribers.GoogleTranscriber;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming.Module;

public sealed class StreamingServiceModule(IServiceProvider moduleServices)
    : HostModule<StreamingSettings>(moduleServices), IWebServerModule
{
    public void ConfigureApp(WebApplication app)
    {
        if (!HostInfo.HasRole(HostRole.Api))
            return;

        // SignalR hub endpoints
        app.MapHub<StreamHub>("/api/hub/streams");
    }

    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IAudioStreamingBackend>() is ServiceMode.Client;

        // SignalR hub — audio-only legacy endpoint (video has moved to Fusion RPC)
        if (rpcHost.IsApiHost) {
            var signalR = services.AddSignalR(options => {
                options.StreamBufferCapacity = 20;
                options.EnableDetailedErrors = true; // Enable for debugging
                options.StatefulReconnectBufferSize = 2000;
            });
            signalR.AddJsonProtocol();
            signalR.AddMessagePackProtocol();
            services.AddScoped<StreamHub>();
        }

        rpcHost.AddApi<ILiveAudioStreams, LiveAudioStreams>();
        rpcHost.AddApi<ILiveVideoStreams, LiveVideoStreams>();
        rpcHost.AddApi<ILiveSessions, LiveSessions>();
        rpcHost.AddBackend<IAudioStreamingBackend, AudioStreamingBackend>();
        rpcHost.AddBackend<IVideoStreamingBackend, VideoStreamingBackend>();
		rpcHost.AddBackend<ILiveAudioBackend, LiveAudioBackend>();
        rpcHost.AddBackend<ILiveVideoBackend, LiveVideoBackend>();
        rpcHost.AddBackend<ILiveSessionsBackend, LiveSessionsBackend>();
        services.AddSingleton<LiveStreamAccess>();
        services.AddSingleton<RemoteVideoStreamCache>();
        services.AddSingleton<RemoteAudioStreamCache>();
        services.TryAddSingleton<AudioSettings>(); // AudioSettings are not configured now
        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Internal services
        services.AddSingleton(_ => new AudioSettings()); // Used in BlazorUIAppModule as well
        services.AddSingleton<ITranscriberFactory, TranscriberFactory>();
        services.AddSingleton<GoogleTranscriber>();
        services.AddSingleton<DeepgramTranscriber>();
        services.AddSingleton<FakeTranscriber>();
        services.AddSingleton<AudioSegmentSaver>();

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<StreamingContext>(services);

        // Disable Deepgram logging
        Deepgram.Logger.Log.Initialize(Deepgram.Logger.LogLevel.Disable);
    }
}
