using ActualChat.SignalR.Client;
using ActualChat.Transcription;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Audio;

public class AudioHubBackendClient : HubClientBase,
    IAudioStreamClient,
    ITranscriptStreamClient
{
    private readonly Worker _worker;
    private readonly ConcurrentDictionary<Symbol, (Task<Unit> WhenReceived, Task<Unit> WhenCompleted)> _ackTaskMap = new ();

    internal AudioHubBackendClient(string address, int port, IServiceProvider services)
        : base(BuildUri(address, port), services)
    {
        _worker = new Worker(this);
        _worker.Start();
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

    public async Task<Task> Write(Symbol streamId, IAsyncEnumerable<byte[]> audioStream, CancellationToken cancellationToken)
    {
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        var whenReceived = TaskSource.New<Unit>(true).Task;
        var whenCompleted = TaskSource.New<Unit>(true).Task;
        _ackTaskMap[streamId] = (whenReceived, whenCompleted);
        await connection.SendAsync("WriteAudioStream", streamId.Value, audioStream, cancellationToken).ConfigureAwait(false);
        await whenReceived.ConfigureAwait(false);

        // ReSharper disable MethodSupportsCancellation
        _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => _ackTaskMap.TryRemove(streamId, out var _), TaskScheduler.Default);
        return whenCompleted;
    }

    public async Task<Task> Write(Symbol streamId, IAsyncEnumerable<Transcript> transcriptStream, CancellationToken cancellationToken)
    {
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        var whenReceived = TaskSource.New<Unit>(true).Task;
        var whenCompleted = TaskSource.New<Unit>(true).Task;
        _ackTaskMap[streamId] = (whenReceived, whenCompleted);
        await connection.SendAsync("WriteTranscriptStream", streamId.Value, transcriptStream, cancellationToken).ConfigureAwait(false);
        await whenReceived.ConfigureAwait(false);

        // ReSharper disable MethodSupportsCancellation
        _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => _ackTaskMap.TryRemove(streamId, out var _), TaskScheduler.Default);
        return whenCompleted;
    }

    protected override async Task DisposeAsyncCore()
    {
        await base.DisposeAsyncCore().ConfigureAwait(false);
        await _worker.DisposeSilentlyAsync().ConfigureAwait(false);
    }

    private static Uri BuildUri(string address, int port)
    {
        var protocol = port.ToString(CultureInfo.InvariantCulture).EndsWith("80", StringComparison.Ordinal)
            ? "http"
            : "https";

        return new Uri($"{protocol}://{address}:{port}/backend/hub/audio");
    }

    private async IAsyncEnumerable<Ack> ReadAckStream([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var connection = await GetHubConnection(cancellationToken).ConfigureAwait(false);
        var ackStream = connection
            .StreamAsync<Ack>("ReadAckStream", cancellationToken);
        await foreach(var ack in ackStream.ConfigureAwait(false))
            yield return ack;
    }

    private class Worker : WorkerBase
    {
        private AudioHubBackendClient Owner { get; }

        public Worker(AudioHubBackendClient owner)
            => Owner = owner;

        protected override async Task RunInternal(CancellationToken cancellationToken)
        {
            for (int retryCount = 0; retryCount < 10; retryCount++)
                try {

                    var ackStream = Owner.ReadAckStream(cancellationToken);
                    await foreach (var ack in ackStream.ConfigureAwait(false))
                        if (ack.Type == AckType.Received) {
                            if (Owner._ackTaskMap.TryGetValue(ack.StreamId, out var ackTasks))
                                TaskSource.For(ackTasks.WhenReceived).TrySetResult(default);
                        } else if (ack.Type == AckType.Completed)
                            if (Owner._ackTaskMap.TryRemove(ack.StreamId, out var ackTasks))
                                TaskSource.For(ackTasks.WhenCompleted).TrySetResult(default);
                    await Task.Delay(100 * Random.Shared.Next(15), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException) {
                    Owner.Log.LogWarning(e, "Error reading ack stream");
                }
            Owner.Log.LogError("Too many retries while trying to read ack stream");
        }
    }
}
