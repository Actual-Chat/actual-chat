using ActualChat.Audio;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Redis.Module;
using ActualChat.Live;
using ActualChat.Streaming.Services;
using ActualChat.Streaming.Services.Transcribers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GoogleTranscriber = ActualChat.Streaming.Services.Transcribers.GoogleTranscriber;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming.Module;

public sealed class StreamingServiceModule(IServiceProvider moduleServices)
    : HostModule<StreamingSettings>(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IAudioStreamingBackend>() is ServiceMode.Client;

        rpcHost.AddApi<IStreamServer, StreamServer>();
        rpcHost.AddApi<ILiveAudioStreams, LiveAudioStreams>();
        rpcHost.AddApi<ILiveVideoStreams, LiveVideoStreams>();
        rpcHost.AddBackend<IAudioStreamingBackend, AudioStreamingBackend>();
        rpcHost.AddBackend<IVideoStreamingBackend, VideoStreamingBackend>();
		rpcHost.AddBackend<ILiveAudioBackend, LiveAudioBackend>();
        rpcHost.AddBackend<ILiveVideoBackend, LiveVideoBackend>();
        services.AddSingleton<StreamLatencyStore>();
        services.AddSingleton<RemoteVideoStreamCache>();
        services.AddSingleton<RemoteAudioStreamCache>();
        services.AddSingleton<IStreamClient, StreamBackendClient>(); // Client for IAudioStreamingBackend
        services.TryAddSingleton<AudioSettings>(); // AudioSettings are not configured now
        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Internal services
        services.AddSingleton(_ => new AudioSettings()); // Used in BlazorUIAppModule as well
        services.AddSingleton<ITranscriberFactory, TranscriberFactory>();
        services.AddSingleton<GoogleTranscriber>();
        services.AddSingleton<DeepgramTranscriber>();
        services.AddSingleton<AudioSegmentSaver>();

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<StreamingContext>(services);

        // Disable Deepgram logging
        Deepgram.Logger.Log.Initialize(Deepgram.Logger.LogLevel.Disable);
    }
}
