using ActualChat.SignalR.Client;
using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Audio;

public class AudioHubBackendClient : HubClientBase,
    IAudioStreamServer,
    ITranscriptStreamServer
{
    public AudioHubBackendClient(IServiceProvider services)
        : base("backend/hub/audio", services)
    {
    }

    public async IAsyncEnumerable<byte[]> Read(
        Symbol streamId,
        TimeSpan skipTo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        var audioStream = connection
            .StreamAsync<byte[]>("GetAudioStream", streamId.Value, skipTo, cancellationToken)
            .WithBuffer(AudioStreamServer.StreamBufferSize, cancellationToken);
        await foreach(var chunk in audioStream.ConfigureAwait(false))
            yield return chunk;
    }

    public async IAsyncEnumerable<Transcript> Read(
        Symbol streamId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        var transcriptStream = connection
            .StreamAsync<Transcript>("GetTranscriptStream", streamId.Value, cancellationToken)
            .WithBuffer(TranscriptStreamServer.StreamBufferSize, cancellationToken);

        // var enumerator = transcriptStream.GetAsyncEnumerator(cancellationToken);
        // if (!await enumerator.MoveNextAsync())
        //     return AsyncEnumerable.Empty<Transcript>();

        // yield return enumerator.Current;
        await foreach(var chunk in transcriptStream.ConfigureAwait(false))
            yield return chunk;
    }

    public async Task Write(Symbol streamId, IAsyncEnumerable<byte[]> audioStream, CancellationToken cancellationToken)
    {
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        await connection.SendAsync("WriteAudioStream", streamId.Value, audioStream, cancellationToken).ConfigureAwait(false);
    }

    public async Task Write(Symbol streamId, IAsyncEnumerable<Transcript> transcriptStream, CancellationToken cancellationToken)
    {
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        await connection.SendAsync("WriteTranscriptStream", streamId.Value, transcriptStream, cancellationToken).ConfigureAwait(false);
    }
}
