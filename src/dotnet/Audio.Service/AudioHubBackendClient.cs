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

    public async Task<Option<IAsyncEnumerable<byte[]>>> Read(
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        var audioStream = connection
            .StreamAsync<byte[]>("GetAudioStream", streamId.Value, skipTo, cancellationToken)
            .WithBuffer(AudioStreamServer.StreamBufferSize, cancellationToken);

        var enumerator = audioStream.GetAsyncEnumerator(cancellationToken);
        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
            return Option<IAsyncEnumerable<byte[]>>.None;

        return Option<IAsyncEnumerable<byte[]>>.Some(Iterator(enumerator));

        async IAsyncEnumerable<byte[]> Iterator(IAsyncEnumerator<byte[]> enumerator1)
        {
            yield return enumerator.Current;

            while (await enumerator1.MoveNextAsync().ConfigureAwait(false))
                yield return enumerator.Current;
        }
    }

    public async Task<Option<IAsyncEnumerable<Transcript>>> Read(
        Symbol streamId,
        CancellationToken cancellationToken)
    {
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        var transcriptStream = connection
            .StreamAsync<Transcript>("GetTranscriptStream", streamId.Value, cancellationToken)
            .WithBuffer(TranscriptStreamServer.StreamBufferSize, cancellationToken);

        var enumerator = transcriptStream.GetAsyncEnumerator(cancellationToken);
        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
            return Option<IAsyncEnumerable<Transcript>>.None;

        return Option<IAsyncEnumerable<Transcript>>.Some(Iterator(enumerator));

        async IAsyncEnumerable<Transcript> Iterator(IAsyncEnumerator<Transcript> enumerator1)
        {
            yield return enumerator.Current;

            while (await enumerator1.MoveNextAsync().ConfigureAwait(false))
                yield return enumerator.Current;
        }
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
