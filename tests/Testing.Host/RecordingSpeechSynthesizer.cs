using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Testing.Host;

/// <summary>
/// Records every text chunk a dub sends to the synthesizer before speaking it like
/// <see cref="FakeSpeechSynthesizer"/> does, so a test can assert what was said.
/// </summary>
public sealed class RecordingSpeechSynthesizer(IServiceProvider services) : ISpeechSynthesizer
{
    private readonly object _lock = new();
    private readonly List<(string StreamId, string Text)> _chunks = [];
    private TaskCompletionSource _whenChangedSource = TaskCompletionSourceExt.New();

    private FakeSpeechSynthesizer Inner { get; } = new(services);

    // Test-only: while set, a one-shot synthesis holds its frames until the task returned for its
    // text completes, so a test can observe a dub that's still being made
    public Func<string, Task>? OneShotGate { get; set; }

    public IReadOnlyList<string> GetChunks(string streamId)
    {
        lock (_lock)
            return _chunks.Where(x => x.StreamId == streamId).Select(x => x.Text).ToList();
    }

    public async Task<IReadOnlyList<string>> WhenSpoken(
        string streamId,
        int minChunkCount,
        CancellationToken cancellationToken)
    {
        while (true) {
            Task whenChanged;
            lock (_lock) {
                var chunks = _chunks.Where(x => x.StreamId == streamId).Select(x => x.Text).ToList();
                if (chunks.Count >= minChunkCount)
                    return chunks;

                whenChanged = _whenChangedSource.Task;
            }
            await whenChanged.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task Synthesize(
        string streamId,
        ChannelReader<string> text,
        SpeechSynthesisOptions options,
        ChannelWriter<AudioFrame> output,
        CancellationToken cancellationToken = default)
    {
        var forwarded = Channel.CreateUnbounded<string>();
        var recordTask = ForwardAndRecord(streamId, text, forwarded.Writer, cancellationToken);
        await Inner.Synthesize(streamId, forwarded.Reader, options, output, cancellationToken).ConfigureAwait(false);
        await recordTask.ConfigureAwait(false);
    }

    public async Task<AudioSource> Synthesize(
        string text,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default)
    {
        Record(OneShotStreamId(options.Language, text), text);
        var inner = await Inner.Synthesize(text, options, cancellationToken).ConfigureAwait(false);
        if (OneShotGate is not { } gate)
            return inner;

        return new AudioSource(
            inner.CreatedAt,
            inner.Format,
            GateFrames(gate.Invoke(text), inner, cancellationToken),
            TimeSpan.Zero,
            inner.Log,
            cancellationToken);
    }

    public static string OneShotStreamId(Language language, string text)
        => $"{language.Value}:{text.GetHashCode()}";

    // Private methods

    private async Task ForwardAndRecord(
        string streamId,
        ChannelReader<string> text,
        ChannelWriter<string> forwarded,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            await foreach (var chunk in text.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                Record(streamId, chunk);
                await forwarded.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            error = e;
            throw;
        }
        finally {
            forwarded.TryComplete(error);
        }
    }

    private static async IAsyncEnumerable<AudioFrame> GateFrames(
        Task gate,
        AudioSource inner,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var frame in inner.GetFrames(cancellationToken).ConfigureAwait(false))
            yield return frame;
    }

    private void Record(string streamId, string chunk)
    {
        lock (_lock) {
            _chunks.Add((streamId, chunk));
            var whenChangedSource = _whenChangedSource;
            _whenChangedSource = TaskCompletionSourceExt.New();
            whenChangedSource.TrySetResult();
        }
    }
}
