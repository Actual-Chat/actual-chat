using ActualChat.Security;
using ActualChat.Transcription;
using ActualChat.Web;
using Microsoft.AspNetCore.SignalR;

namespace ActualChat.Audio;

public class AudioHub(IServiceProvider services) : Hub
{
    private IServiceProvider Services { get; } = services;
    private IAudioProcessor AudioProcessor { get; } = services.GetRequiredService<IAudioProcessor>();
    private IAudioStreamServer AudioStreamServer { get; } = services.GetRequiredService<IAudioStreamServer>();
    private ITranscriptStreamServer TranscriptStreamServer { get; } = services.GetRequiredService<ITranscriptStreamServer>();
    private ISecureTokensBackend SecureTokensBackend { get; } = services.GetRequiredService<ISecureTokensBackend>();
    private OtelMetrics Metrics { get; } = services.GetRequiredService<OtelMetrics>();
    private ILogger Log { get; } = services.LogFor<AudioHub>();

    public IAsyncEnumerable<byte[]> GetAudioStream(
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        // We're doing this fairly complex processing via tasks & channels only
        // because "async IAsyncEnumerable<..>" methods can't contain
        // "yield return" inside "catch" blocks, and we need this here.
        var target = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(Constants.Queues.OpusStreamConverterQueueSize) {
                SingleWriter = true,
                SingleReader = true,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        _ = BackgroundTask.Run(async () => {
            var counter = 0;
            var currentSkipTo = skipTo;
            try {
                while (!cancellationToken.IsCancellationRequested)
                    try {
                        var stream = await AudioStreamServer.Read(streamId, currentSkipTo, cancellationToken)
                            .ConfigureAwait(false);
                        await foreach (var chunk in stream.ConfigureAwait(false)) {
                            counter++;
                            await target.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                        }
                        return;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                        currentSkipTo = skipTo.Add(TimeSpan.FromMilliseconds(20 * counter));
                        Log.LogWarning("Retry reading audio stream {StreamId} with offset {SkipTo}", streamId, currentSkipTo);
                    }
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException e) {
                target.Writer.TryComplete(e);
                throw;
            }
            catch (Exception e) {
                Log.LogError(e, "Error reading audio stream");
                target.Writer.TryComplete(e);
                throw;
            }
            finally {
                target.Writer.TryComplete();
            }
        }, cancellationToken);

        return target.Reader.ReadAllAsync(cancellationToken);
    }

    public Task ReportLatency(TimeSpan latency, CancellationToken cancellationToken)
    {
        Metrics.AudioLatency.Record(latency.Ticks / 10000f);
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<TranscriptDiff> GetTranscriptDiffStream(
        string streamId,
        CancellationToken cancellationToken)
    {
        // We're doing this fairly complex processing via tasks & channels only
        // because "async IAsyncEnumerable<..>" methods can't contain
        // "yield return" inside "catch" blocks, and we need this here.
        var target = Channel.CreateBounded<TranscriptDiff>(
            new BoundedChannelOptions(Constants.Queues.OpusStreamConverterQueueSize) {
                SingleWriter = true,
                SingleReader = true,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        _ = BackgroundTask.Run(async () => {
            try {
                while (!cancellationToken.IsCancellationRequested)
                    try {
                        var stream = await TranscriptStreamServer.Read(streamId, cancellationToken)
                            .ConfigureAwait(false);
                        await foreach (var chunk in stream.ConfigureAwait(false)) {
                            await target.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                        }
                        return;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                        Log.LogWarning("Retry reading transcript stream {StreamId}", streamId);
                    }
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException e) {
                target.Writer.TryComplete(e);
                throw;
            }
            catch (Exception e) {
                Log.LogError(e, "Error reading transcript stream");
                target.Writer.TryComplete(e);
                throw;
            }
            finally {
                target.Writer.TryComplete();
            }
        }, cancellationToken);

        return target.Reader.ReadAllAsync(cancellationToken);
    }

    public Task ProcessAudioChunks(
        string sessionToken,
        string chatId,
        string repliedChatEntryId,
        double clientStartOffset,
        int preSkipFrames,
        IAsyncEnumerable<byte[][]> audioStream)
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!
        => ProcessAudio(
            sessionToken,
            chatId,
            repliedChatEntryId,
            clientStartOffset,
            preSkipFrames,
            audioStream.SelectMany(c => c.AsAsyncEnumerable()));

    public async Task ProcessAudio(
        string sessionToken,
        string chatId,
        string repliedChatEntryId,
        double clientStartOffset,
        int preSkipFrames,
        IAsyncEnumerable<byte[]> audioStream)
    {
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!

        var httpContext = Context.GetHttpContext()!;
        var session = GetSessionFromToken(sessionToken) ?? httpContext.GetSessionFromCookie();

        var audioRecord = AudioRecord.New(new Session(session.Id), new ChatId(chatId), clientStartOffset, new ChatEntryId(repliedChatEntryId));
        var frameStream = audioStream
            .Select((packet, i) => new AudioFrame {
                Data = packet,
                Offset = TimeSpan.FromMilliseconds(i * 20), // we support only 20-ms packets
            });

        var trimDuration = Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5);
        var cancelDuration = Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(10);

        using var cancelCts = new CancellationTokenSource(cancelDuration);
        using var trimCts = new CancellationTokenSource(trimDuration);
        frameStream = frameStream.TrimOnCancellation(trimCts.Token);

        await AudioProcessor
            .ProcessAudio(audioRecord, preSkipFrames, frameStream, cancelCts.Token)
            .ConfigureAwait(false);
    }

    public Task<string> Ping()
        => Task.FromResult("Pong");

    private Session? GetSessionFromToken(string recorderToken)
    {
        // [Obsolete("2023.07: Legacy clients use 'default' value.")]
        if (recorderToken.IsNullOrEmpty() || OrdinalEquals(recorderToken, "default"))
            return null;

        // [Obsolete("2023.07: Legacy clients may use Session.Id instead of session token.")]
        if (!SecureToken.HasValidPrefix(recorderToken))
            return new Session(recorderToken).RequireValid();

        return SecureTokensBackend.ParseSessionToken(recorderToken);
    }
}
