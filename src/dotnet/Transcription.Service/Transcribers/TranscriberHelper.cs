using ActualChat.Audio;

namespace ActualChat.Transcription;

public static class TranscriberHelper
{
    private const string SilenceResourceName = "ActualChat.Transcription.data.silence.opuss";
    private static readonly Lock Sync = new();
    private static Task<AudioSource>? _silenceAudioSourceTask;
    private static AudioSource? _silenceAudioSource;

    public static async Task<AudioSource> GetSilenceAudioSource(IServiceProvider services)
    {
        if (_silenceAudioSource != null)
            return _silenceAudioSource;

        var task = LoadSilenceAudio(services);
        _silenceAudioSource = await task.ConfigureAwait(false);
        return _silenceAudioSource;
    }

    public static async Task WhenPushAndRead(
        Task pushTask,
        Task readTask,
        CancellationTokenSource cancellationTokenSource)
    {
        // Both tasks share cancellationTokenSource, so cancelling it stops whichever side still runs.
        var first = await Task.WhenAny(pushTask, readTask).ConfigureAwait(false);
        var mustCancel = first.IsFaulted || first == readTask;
        if (mustCancel)
            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);

        await pushTask.SilentAwait(false);
        await readTask.SilentAwait(false);
        // Whichever side finished first holds the cause; the other one only saw the fallout,
        // e.g. a write into a connection the server had already torn down.
        await first.ConfigureAwait(false);
        if (!mustCancel)
            await readTask.ConfigureAwait(false);
    }

    public static AudioSource AddSilentPrefixAndSuffix(
        AudioSource audioSource,
        AudioSource silenceAudioSource,
        TimeSpan prefix,
        TimeSpan suffix,
        CancellationToken cancellationToken)
        => silenceAudioSource
            .Take(prefix, cancellationToken)
            .Concat(audioSource, cancellationToken)
            .ConcatUntil(silenceAudioSource, suffix, cancellationToken);

    private static Stream GetSilenceStream()
    {
        var assembly = typeof(TranscriberHelper).Assembly;
        return assembly.GetManifestResourceStream(SilenceResourceName)
            ?? throw StandardError.Internal(
                $"Embedded resource '{SilenceResourceName}' is missing from {assembly.GetName().Name}.");
    }

    private static Task<AudioSource> LoadSilenceAudio(IServiceProvider services)
    {
        lock (Sync) {
            if (_silenceAudioSourceTask != null)
                return _silenceAudioSourceTask;

            _silenceAudioSourceTask = Load(services);
            return _silenceAudioSourceTask;
        }

        static async Task<AudioSource> Load(IServiceProvider services)
        {
            var silenceChunks = await GetSilenceStream()
                .ReadByteStream(true)
                .ToListAsync()
                .ConfigureAwait(false);

            var converter = new ActualOpusStreamConverter(
                MomentClockSet.Default,
                services.LogFor<ActualOpusStreamConverter>());

            return await converter
                .FromByteStream(silenceChunks.AsAsyncEnumerable(), CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}
