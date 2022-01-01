using ActualChat.Media;

namespace ActualChat.Audio.Processing;

public sealed class AudioSplitter : AudioProcessorBase
{
    private ILogger OpenAudioSegmentLog { get; }

    public AudioSplitter(IServiceProvider services) : base(services)
        => OpenAudioSegmentLog = Services.LogFor<OpenAudioSegment>();

    public IAsyncEnumerable<OpenAudioSegment> GetSegments(
        AudioRecord audioRecord,
        IAsyncEnumerable<RecordingPart> recordingStream,
        CancellationToken cancellationToken)
    {
        var openSegmentChannel =
            Channel.CreateUnbounded<OpenAudioSegment>(new UnboundedChannelOptions { SingleWriter = true });

        _ = BackgroundTask.Run(
            () => WriteSegments(audioRecord, recordingStream, openSegmentChannel, cancellationToken),
            Log, $"{nameof(GetSegments)} failed.", CancellationToken.None);

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
            var currentSegment = await NewAudioSegment(index, channel.Reader.ReadAllAsync(cancellationToken)).ConfigureAwait(false);
            var lastStartOffset = TimeSpan.FromSeconds(0);

            await foreach (var recordingPart in recordingStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                switch (recordingPart.EventKind) {
                    case RecordingEventKind.Pause:
                        if (recordingPart.Offset is {} offset1)
                            currentSegment.SetAudibleDuration(offset1 - lastStartOffset);
                        if (!channel.Writer.TryComplete())
                            audioLog.LogWarning("WriteSegments: duplicate Pause?");
                        continue;
                    case RecordingEventKind.Resume:
                        if (index > 0) {
                            channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
                                { SingleWriter = true, SingleReader = true });
                            var format = await firstSegmentFormatSource.Task.ConfigureAwait(false);
                            var formatChunk = Convert.FromBase64String(format.CodecSettings);
                            await channel.Writer.WriteAsync(formatChunk, cancellationToken).ConfigureAwait(false);
                            currentSegment = await NewAudioSegment(index, channel.Reader.ReadAllAsync(cancellationToken)).ConfigureAwait(false);
                        }
                        currentSegment.SetRecordedAt(recordingPart.RecordedAt);
                        lastStartOffset = recordingPart.Offset.GetValueOrDefault();
                        index++;
                        continue;
                    default:
                        if (recordingPart.Data == null)
                            audioLog.LogWarning("WriteSegments: empty recording data");
                        else {
                            var canWrite = await channel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false);
                            if (!canWrite)
                                audioLog.LogWarning("WriteSegments: data came after Pause, but before Resume");
                            else
                                await channel.Writer.WriteAsync(recordingPart.Data, cancellationToken).ConfigureAwait(false);
                        }
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

        async ValueTask<OpenAudioSegment> NewAudioSegment(int segmentIndex, IAsyncEnumerable<byte[]> segmentRecordingStream)
        {
            var audio = new AudioSource(segmentRecordingStream, TimeSpan.Zero, audioLog, cancellationToken);
            var openAudioSegment = new OpenAudioSegment(segmentIndex, audioRecord, audio, OpenAudioSegmentLog);
            _ = BackgroundTask.Run(async () => {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(3.5));
                using var linkedCts = timeoutCts.Token.LinkWith(cancellationToken);
                var linkedToken = linkedCts.Token;
                try {
                    await audio.WhenFormatAvailable.WithFakeCancellation(linkedToken).ConfigureAwait(false);
                    if (!firstSegmentFormatSource.Task.IsCompleted)
                        firstSegmentFormatSource.SetResult(audio.Format);

                    await audio.WhenDurationAvailable.WithFakeCancellation(linkedToken).ConfigureAwait(false);
                    // We don't want to wait for too long here to finalize the entry,
                    // + it should actually come almost instantly
                    await openAudioSegment.AudibleDurationTask
                        .WithTimeout(TimeSpan.FromSeconds(1), linkedToken)
                        .ConfigureAwait(false);
                    openAudioSegment.Close(audio.Duration);
                }
                catch (Exception e) {
                    openAudioSegment.TryClose(e);
                }
            }, Log, $"{nameof(NewAudioSegment)} processing failed");

            await writer.WriteAsync(openAudioSegment, cancellationToken).ConfigureAwait(false);
            return openAudioSegment;
        }
    }
}
