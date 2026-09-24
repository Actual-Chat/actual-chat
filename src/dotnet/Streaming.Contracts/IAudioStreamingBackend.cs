using ActualChat.Attributes;
using ActualChat.Audio;
using ActualChat.Sharding;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// Backend service for audio and transcript streaming.
/// </summary>
[BackendService(nameof(HostRole.StreamingBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(ShardScheme.StreamingBackend))]
public interface IAudioStreamingBackend : IComputeService, IBackendService
{
    // Language-suffixed transcript stream ids resolve to their base stream's chat.
    Task<ChatId?> GetChatId(StreamId streamId, CancellationToken cancellationToken);

    // Starts speaking a text stream before anyone asks for it, so the first listener does not wait
    // out the synthesizer's start-up. A no-op once it is already speaking.
    Task PrewarmSpeech(StreamId streamId, CancellationToken cancellationToken);

    // How far this stream's synthesized voice is behind the text it was given, or null when
    // nothing is speaking it - which is also the answer "nobody is listening".
    Task<TimeSpan?> GetSpeechBacklog(StreamId streamId, CancellationToken cancellationToken);

    // The entry this stream posted, once it exists - null while the pipeline is still deciding
    // there is anything to post. A call-by-call producer has no other way to name its own message.
    Task<ChatEntryId?> GetStreamedEntryId(StreamId streamId, CancellationToken cancellationToken);

    // Folded from the same memoized diff stream GetTranscript replays, and invalidated when the
    // next diff lands. NoCache because the value lives in the memory of one node - the one named
    // by streamId.NodeRef - so a client must never serve it from its own cache.
    [ComputeMethod]
    [RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<Transcript?> GetTranscriptSnapshot(StreamId streamId, CancellationToken cancellationToken);

    Task<RpcStream<AudioFrame>?> GetAudio(
        StreamId streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken);

    Task<RpcStream<TranscriptDiff>?> GetTranscript(
        StreamId streamId,
        CancellationToken cancellationToken);

    Task PushTranscript(
        StreamId streamId,
        RpcStream<TranscriptDiff> diffStream,
        CancellationToken cancellationToken);

    // A transcript with no audio behind it. Carries the speaker, which PushTranscript can leave to
    // ProcessAudio but nothing else would register - and without it the text cannot be spoken.
    Task PushTextTranscript(
        StreamId streamId,
        ChatId chatId,
        AuthorId authorId,
        RpcStream<TranscriptDiff> diffStream,
        CancellationToken cancellationToken);

    Task ProcessAudio(
        AudioRecord record,
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken);

    // The producer supplies the transcript, so server ASR is bypassed. Chunk offsets are
    // positions in the producer's own audio; a chunk without one is pinned to the audio
    // ingested so far.
    Task ProcessAudioWithTranscript(
        AudioRecord record,
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        RpcStream<ExternalTranscriptChunk> transcriptStream,
        Language? language,
        CancellationToken cancellationToken);
}
