using ActualChat.Audio;

namespace ActualChat.Streaming.Services.Transcribers;

public static class TranscriberHelper
{
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
            var silenceChunks = await typeof(TranscriberHelper).Assembly
                .GetManifestResourceStream("ActualChat.Streaming.data.silence.opuss")!
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
