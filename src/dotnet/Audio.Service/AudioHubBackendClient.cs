using ActualChat.SignalR;
using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Audio;

public class AudioHubBackendClient : HubClientBase,
    IAudioStreamClient,
    ITranscriptStreamClient
{
    public int AudioStreamBufferSize { get; init; } = 64;
    public int TranscriptStreamBufferSize { get; init; } = 16;

    internal AudioHubBackendClient(string address, int port, IServiceProvider services)
        : base(GetHubUrl(address, port), services)
    { }

    public async Task<IAsyncEnumerable<byte[]>> Read(
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var connection = await GetConnection(cancellationToken).ConfigureAwait(false);
        var stream = connection
            .StreamAsync<byte[]>("GetAudioStream", streamId.Value, skipTo, cancellationToken)
            .WithBuffer(AudioStreamBufferSize, cancellationToken);
        return stream;
    }

    public async Task<IAsyncEnumerable<TranscriptDiff>> Read(
        Symbol streamId,
        CancellationToken cancellationToken)
    {
        var connection = await GetConnection(cancellationToken).ConfigureAwait(false);
        var stream = connection
            .StreamAsync<TranscriptDiff>("GetTranscriptDiffStream", streamId.Value, cancellationToken)
            .WithBuffer(TranscriptStreamBufferSize, cancellationToken);
        return stream;
    }

    public async Task Write(Symbol streamId, IAsyncEnumerable<byte[]> stream, CancellationToken cancellationToken)
    {
        var connection = await GetConnection(cancellationToken).ConfigureAwait(false);
        // wait for stream upload completion
        await connection.InvokeAsync("WriteAudioStream", streamId.Value, stream, cancellationToken).ConfigureAwait(false);
    }

    public async Task Write(Symbol streamId, IAsyncEnumerable<TranscriptDiff> stream, CancellationToken cancellationToken)
    {
        var connection = await GetConnection(cancellationToken).ConfigureAwait(false);
        // wait for stream upload completion
        await connection.InvokeAsync("WriteTranscriptDiffStream", streamId.Value, stream, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private static string GetHubUrl(string address, int port)
    {
        var protocol = port.Format().OrdinalEndsWith("80")
            ? "http"
            : "https";
        return $"{protocol}://{address}:{port}/backend/hub/audio";
    }
}
