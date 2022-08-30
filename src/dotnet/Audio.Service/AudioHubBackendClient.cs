using ActualChat.SignalR.Client;
using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Audio;

public class AudioHubBackendClient : HubClientBase,
    IAudioStreamClient,
    ITranscriptStreamClient
{
    protected int AudioStreamBufferSize { get; init; } = 64;
    protected int TranscriptStreamBufferSize { get; init; } = 16;

    internal AudioHubBackendClient(string address, int port, IServiceProvider services)
        : base(BuildUri(address, port), services)
    { }

    public async Task<IAsyncEnumerable<byte[]>> Read(
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        var stream = connection
            .StreamAsync<byte[]>("GetAudioStream", streamId.Value, skipTo, cancellationToken)
            .WithBuffer(AudioStreamBufferSize, cancellationToken);
        return stream;
    }

    public async Task<IAsyncEnumerable<Transcript>> Read(
        Symbol streamId,
        CancellationToken cancellationToken)
    {
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        var stream = connection
            .StreamAsync<Transcript>("GetTranscriptStream", streamId.Value, cancellationToken)
            .WithBuffer(TranscriptStreamBufferSize, cancellationToken);
        return stream;
    }

    public async Task Write(Symbol streamId, IAsyncEnumerable<byte[]> stream, CancellationToken cancellationToken)
    {
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        await connection.SendAsync("WriteAudioStream", streamId.Value, stream, cancellationToken).ConfigureAwait(false);
    }

    public async Task Write(Symbol streamId, IAsyncEnumerable<Transcript> stream, CancellationToken cancellationToken)
    {
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        await connection.SendAsync("WriteTranscriptStream", streamId.Value, stream, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private static Uri BuildUri(string address, int port)
    {
        var protocol = port.ToString(CultureInfo.InvariantCulture).EndsWith("80", StringComparison.Ordinal)
            ? "http"
            : "https";
        return new Uri($"{protocol}://{address}:{port}/backend/hub/audio");
    }
}
