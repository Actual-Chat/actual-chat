using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace ActualChat.Distribution.Module
{
    public sealed class HubRegistrar : IHubRegistrar
    {
        public void RegisterHubs(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapHub<AudioStreamingService>("api/audio-stream");
            endpoints.MapHub<VideoStreamingService>("api/video-stream");
            endpoints.MapHub<TranscriptStreamingService>("api/transcription-stream");
        }
    }
}