namespace ActualChat.Transcription;

public sealed record SonioxVoice(string Id, string Name, bool IsReady, bool IsFailed);

/// <summary>
/// A speaker's clone on Soniox's voice-cloning API - create/poll/list/delete. Split out from its
/// implementation (Transcription.Service's SonioxVoicesClient) so VoicePool (Streaming.Service)
/// depends on this interface only.
/// </summary>
public interface ISonioxVoices
{
    Task<SonioxVoice> Create(string name, Stream wav, CancellationToken cancellationToken);
    Task<SonioxVoice?> Get(string id, CancellationToken cancellationToken);
    Task<ApiArray<SonioxVoice>> List(CancellationToken cancellationToken);
    Task Delete(string id, CancellationToken cancellationToken);
}
