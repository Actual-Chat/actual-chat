using ActualChat.Users;

namespace ActualChat.Streaming;

/// <summary>
/// The speaker side of voice cloning: what the "Use my own voice" setting shows.
/// </summary>
public interface IOwnVoices : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 10)]
    Task<OwnVoiceStatus> GetOwnVoiceStatus(Session session, CancellationToken cancellationToken);
}
