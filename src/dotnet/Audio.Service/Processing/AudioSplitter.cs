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
        var channel = Channel.CreateUnbounded<byte[]>(
            new UnboundedChannelOptions{ SingleWriter = true, SingleReader = true }
        );
        try {
            var currentSegment = await StartNewSegment(index, channel.Reader.ReadAllAsync(cancellationToken)).ConfigureAwait(false);

            await foreach (var recordingPart in recordingStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                switch (recordingPart.EventKind)
                {
                    case RecordingEventKind.Pause:
                        if (recordingPart.Offset is {} offset)
                            currentSegment.SetSilenceOffset(offset);
                        channel.Writer.Complete();
                        continue;
                    case RecordingEventKind.Resume:
                    {
                        if (index > 0) {
                            channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
                                { SingleWriter = true, SingleReader = true });
                            var format = await firstSegmentFormatSource.Task.ConfigureAwait(false);
                            var formatChunk = Convert.FromBase64String(format.CodecSettings);
                            await channel.Writer.WriteAsync(formatChunk, cancellationToken).ConfigureAwait(false);
                            currentSegment = await StartNewSegment(index, channel.Reader.ReadAllAsync(cancellationToken)).ConfigureAwait(false);
                        }
                        if (recordingPart.RecordedAt is {} recordedAt)
                            currentSegment.SetRecordedAt(recordedAt, recordingPart.Offset);

                        index++;
                        continue;
                    }
                    default:
                        if (recordingPart.Data == null)
                            audioLog.LogWarning("WriteSegments: empty recording data");
                        else
                            await channel.Writer.WriteAsync(recordingPart.Data, cancellationToken)
                                .ConfigureAwait(false);
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

        async ValueTask<OpenAudioSegment> StartNewSegment(int segmentIndex, IAsyncEnumerable<byte[]> segmentRecordingStream)
        {
            var audio = new AudioSource(segmentRecordingStream, TimeSpan.Zero, audioLog, cancellationToken);
            var openAudioSegment = new OpenAudioSegment(segmentIndex, audioRecord, audio);
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
            return openAudioSegment;
        }
    }
}
