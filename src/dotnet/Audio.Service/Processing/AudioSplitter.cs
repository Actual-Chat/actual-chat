using ActualChat.Media;

namespace ActualChat.Audio.Processing;

public class AudioSplitter
{
    private IServiceProvider Services { get; }

    public AudioSplitter(IServiceProvider services)
        => Services = services;

    public IAsyncEnumerable<OpenAudioSegment> GetSegments(
        AudioRecord audioRecord,
        IAsyncEnumerable<RecordingPart> recordingStream,
        CancellationToken cancellationToken)
    {
        var openSegmentChannel =
            Channel.CreateUnbounded<OpenAudioSegment>(new UnboundedChannelOptions { SingleWriter = true });

        _ = Task.Run(() => WriteSegments(audioRecord, recordingStream, openSegmentChannel, cancellationToken),
            cancellationToken);

        return openSegmentChannel.Reader.ReadAllAsync(cancellationToken);
    }

    private async Task WriteSegments(
        AudioRecord audioRecord,
        IAsyncEnumerable<RecordingPart> recordingStream,
        ChannelWriter<OpenAudioSegment> writer,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        var firstSegmentFormatSource = new TaskCompletionSource<AudioFormat>();
        var audioLog = Services.LogFor<AudioSource>();
        var index = 0;
        var channel = Channel.CreateUnbounded<RecordingPart>(
            new UnboundedChannelOptions{ SingleWriter = true, SingleReader = true }
        );
        try {
            await StartNewSegment(channel.Reader.ReadAllAsync(cancellationToken)).ConfigureAwait(false);

            await foreach (var recordingPart in recordingStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                switch (recordingPart.Command)
                {
                    case RecordingCommand.Pause:
                        channel.Writer.Complete();
                        continue;
                    case RecordingCommand.Resume:
                    {
                        channel = Channel.CreateUnbounded<RecordingPart>(new UnboundedChannelOptions { SingleWriter = true });
                        var format = await firstSegmentFormatSource.Task.ConfigureAwait(false);
                        var formatPart = new RecordingPart { Data = Convert.FromBase64String(format.CodecSettings) };
                        await channel.Writer.WriteAsync(formatPart, cancellationToken).ConfigureAwait(false);
                        await StartNewSegment(channel.Reader.ReadAllAsync(cancellationToken)).ConfigureAwait(false);
                        continue;
                    }
                    default:
                        await channel.Writer.WriteAsync(recordingPart, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (Exception e) {
            error = e;
        }
        finally {
            channel.Writer.TryComplete(error);
            writer.TryComplete(error);
        }

        async ValueTask StartNewSegment(IAsyncEnumerable<RecordingPart> segmentRecordingStream)
        {
            var audio = new AudioSource(segmentRecordingStream, TimeSpan.Zero, audioLog, cancellationToken);
            var openAudioSegment = new OpenAudioSegment(index++, audioRecord, audio);
            _ = Task.Run(async () => {
                try {
                    await audio.WhenFormatAvailable.ConfigureAwait(false);
                    if (!firstSegmentFormatSource.Task.IsCompleted)
                        firstSegmentFormatSource.SetResult(audio.Format);

                    await audio.WhenDurationAvailable.ConfigureAwait(false);
                    openAudioSegment.Close(audio.Duration);
                }
                catch (Exception e) {
                    openAudioSegment.TryClose(e);
                }
            }, CancellationToken.None);
            await writer.WriteAsync(openAudioSegment, cancellationToken).ConfigureAwait(false);
        }
    }
}
