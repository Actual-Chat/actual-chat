using ActualChat.Channels;

namespace ActualChat.Audio;

public static class AudioSourceHelper
{
    public static async ValueTask<AudioSource> ConvertToAudioSource(
        ChannelReader<AudioSourcePart> reader,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<AudioFrame>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

        var canBeRead = await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
        if (!canBeRead)
            throw new InvalidOperationException("Unable to create AudioSource from empty stream.");

        AudioFormat? format = null;
        while (reader.TryRead(out var audioSourcePart)) {
            format = audioSourcePart.Format ?? throw new InvalidOperationException("AudioSourcePart with Format should be the first");
            break;
        }

        var durationTaskSource = new TaskCompletionSource<TimeSpan>();
        _ = Task.Run(()
            => TransformStreamingFrames(reader, channel, durationTaskSource, cancellationToken), cancellationToken);

        return new AudioSource(format!, durationTaskSource.Task, channel.Memoize(CancellationToken.None));
    }

    private static async ValueTask TransformStreamingFrames(
        ChannelReader<AudioSourcePart> audioSourcePartReader,
        ChannelWriter<AudioFrame> writer,
        TaskCompletionSource<TimeSpan> durationTaskSource,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            ParseSourceParts(audioSourcePartReader);

            while (await audioSourcePartReader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                ParseSourceParts(audioSourcePartReader);
        }
        catch (Exception e) {
            error = e;
        }
        finally {
            writer.TryComplete(error);
            if (error != null)
                durationTaskSource.SetException(error);
            else if (!durationTaskSource.Task.IsCompleted)
                durationTaskSource.SetCanceled(cancellationToken);
        }

        void ParseSourceParts(ChannelReader<AudioSourcePart> reader)
        {
            while (reader.TryRead(out var audioSourcePart)) {
                var (_, frame, duration) = audioSourcePart;
                if (duration != null)
                    durationTaskSource.SetResult(duration.Value);
                if (frame != null)
                    writer.TryWrite(frame);
            }
        }
    }
}
