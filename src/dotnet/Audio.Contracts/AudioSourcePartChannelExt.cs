using ActualChat.Channels;

namespace ActualChat.Audio;

public static class AudioSourcePartChannelExt
{
    public static Task<AudioSource> ToAudioSource(
        this Channel<AudioSourcePart> audioSourceParts,
        CancellationToken cancellationToken)
        => audioSourceParts.Reader.ToAudioSource(cancellationToken);

    public static async Task<AudioSource> ToAudioSource(
        this ChannelReader<AudioSourcePart> audioSourceParts,
        CancellationToken cancellationToken)
    {
        var audioFrames = Channel.CreateUnbounded<AudioFrame>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

        var formatTaskSource = TaskSource.New<AudioFormat>(true);
        var formatTask = formatTaskSource.Task;
        var durationTaskSource = TaskSource.New<TimeSpan>(true);
        var durationTask = durationTaskSource.Task;
        _ = Task.Run(ParseAudioSourceParts, CancellationToken.None);

        var format = await formatTask.ConfigureAwait(false);
        return new AudioSource(format, durationTask, audioFrames.Memoize(CancellationToken.None));

        async Task ParseAudioSourceParts()
        {
            Exception? error = null;
            try {
                while (await audioSourceParts.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                while (audioSourceParts.TryRead(out var part)) {
                    if (durationTask.IsCompleted)
                        throw new InvalidOperationException("AudioSourcePart.Duration part must be the last one.");
                    if (formatTask.IsCompleted) {
                        if (part.Format != null)
                            throw new InvalidOperationException("AudioSourcePart.Format part must be the first one.");
                        if (part.Duration.HasValue)
                            durationTaskSource.SetResult(part.Duration.GetValueOrDefault());
                        else if (part.Frame != null)
                            await audioFrames.Writer.WriteAsync(part.Frame, cancellationToken).ConfigureAwait(false);
                        else
                            throw new InvalidOperationException("AudioSourcePart doesn't have any properties set.");
                    }
                    else {
                        if (part.Format == null)
                            throw new InvalidOperationException("AudioSourcePart.Format part is expected first.");
                        formatTaskSource.SetResult(part.Format);
                    }
                }
                if (!formatTask.IsCompleted)
                    throw new InvalidOperationException("AudioSourcePart.Format part is missing.");
                if (!durationTask.IsCompleted)
                    throw new InvalidOperationException("AudioSourcePart.Duration part is missing.");
            }
            catch (Exception e) {
                error = e;
                throw;
            }
            finally {
                audioFrames.Writer.TryComplete(error);
                if (error == null || error is OperationCanceledException oce) {
                    // We run this block even if there is no error to ensure
                    // task sources are at least cancelled in case they aren't set yet (somehow)
                    formatTaskSource.TrySetCanceled();
                    durationTaskSource.TrySetCanceled();
                }
                else {
                    formatTaskSource.TrySetException(error);
                    durationTaskSource.TrySetException(error);
                }
            }
        }
    }
}
