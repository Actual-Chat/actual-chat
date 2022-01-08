using ActualChat.Media;
using ActualChat.Users;

namespace ActualChat.Audio.Processing;

public sealed class AudioSplitter : AudioProcessorBase
{
    private static readonly TimeSpan MaxSegmentProcessingDuration = TimeSpan.FromMinutes(3.5);
    private static readonly TimeSpan SegmentCancellationDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AudibleDurationWaitDelay = TimeSpan.FromSeconds(1);
    private IChatUserSettings? ChatUserSettings { get; }
    private ILogger OpenAudioSegmentLog { get; }
    private ILogger AudioSourceLog { get; }

    public AudioSplitter(IServiceProvider services) : this(services, false) { }
    public AudioSplitter(IServiceProvider services, bool testMode) : base(services)
    {
        OpenAudioSegmentLog = Services.LogFor<OpenAudioSegment>();
        AudioSourceLog = Services.LogFor<AudioSource>();
        ChatUserSettings = testMode
            ? services.GetService<IChatUserSettings>()
            : services.GetRequiredService<IChatUserSettings>();
    }

    public IAsyncEnumerable<OpenAudioSegment> GetSegments(
        AudioRecord audioRecord,
        Author author,
        IAsyncEnumerable<RecordingPart> recordingStream,
        CancellationToken cancellationToken)
    {
        var openSegments = Channel.CreateUnbounded<OpenAudioSegment>(new UnboundedChannelOptions {
            SingleWriter = true,
            SingleReader = true,
        });

        _ = BackgroundTask.Run(
            () => WriteSegments(audioRecord, author, recordingStream, openSegments, cancellationToken),
            Log, $"{nameof(GetSegments)} failed.", CancellationToken.None);

        return openSegments.Reader.ReadAllAsync(cancellationToken);
    }

    private async Task WriteSegments(
        AudioRecord audioRecord,
        Author author,
        IAsyncEnumerable<RecordingPart> recordingStream,
        ChannelWriter<OpenAudioSegment> segments,
        CancellationToken cancellationToken)
    {
        var session = audioRecord.SessionId.IsNullOrEmpty() ? Session.Null : new Session(audioRecord.SessionId);
        var totalDuration = TimeSpan.Zero;
        var segmentIndex = 0;
        var (segment, channel) = await NewAudioSegment(segmentIndex++).ConfigureAwait(false);
        var firstSegment = segment;
        var formatChunk = (byte[]?) null;
        try {
            await foreach (var recordingPart in recordingStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                switch (recordingPart.EventKind) {
                case RecordingEventKind.Pause:
                    if (segment == null) {
                        Log.LogWarning("WriteSegments: duplicate Pause event");
                        break;
                    }
                    if (recordingPart.Offset is {} audibleDuration)
                        segment.SetAudibleDuration(audibleDuration - totalDuration);
                    channel.Complete();
                    var duration = await segment.Audio.GetDurationTask().ConfigureAwait(false);
                    if (!segment.AudibleDurationTask.IsCompleted)
                        segment.SetAudibleDuration(duration);
                    segment = null;
                    totalDuration += duration;
                    break;
                case RecordingEventKind.Resume:
                    if (segment == null)
                        (segment, channel) = await NewAudioSegment(segmentIndex++).ConfigureAwait(false);
                    if (segment != firstSegment) {
                        // We need to "prepend" the data with formatChunk in this case,
                        // since the audio stream contains just a single copy of it
                        // (in the very beginning)
                        if (formatChunk == null) {
                            var format = await firstSegment.Audio.GetFormatTask().ConfigureAwait(false);
                            formatChunk = Convert.FromBase64String(format.CodecSettings);
                        }
                        await channel.WriteAsync(formatChunk, cancellationToken).ConfigureAwait(false);
                    }
                    segment.SetRecordedAt(recordingPart.RecordedAt);
                    if (recordingPart.Offset is {} segmentOffset)
                        totalDuration = segmentOffset;
                    break;
                case RecordingEventKind.Data:
                    if (segment == null) {
                        Log.LogWarning("WriteSegments: missing Resume event");
                        break;
                    }
                    if (recordingPart.Data == null || recordingPart.Data.Length == 0) {
                        Log.LogWarning("WriteSegments: empty recording data");
                        break;
                    }
                    await channel.WriteAsync(recordingPart.Data, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported {nameof(RecordingEventKind)}.");
                }
            }
        }
        finally {
            segments.TryComplete();
            if (segment != null) {
                channel.TryComplete();
                try {
                    var duration = await segment.Audio.GetDurationTask().ConfigureAwait(false);
                    if (!segment.AudibleDurationTask.IsCompleted)
                        segment.SetAudibleDuration(duration);
                }
                catch {
                    // Intended
                }
            }
        }

        async ValueTask<(OpenAudioSegment Segment, ChannelWriter<byte[]> Channel)> NewAudioSegment(int segmentIndex1)
        {
            var settings = ChatUserSettings != null!
                ? await ChatUserSettings.Get(session, audioRecord.ChatId, cancellationToken).ConfigureAwait(false)
                : null;
            var language = settings.LanguageOrDefault();
            var altLanguage = language.Next();
            var languages = ImmutableArray.Create(language, altLanguage);

            var linkedCts = new LinkedTimeoutTokenSource(cancellationToken,
                MaxSegmentProcessingDuration,
                SegmentCancellationDelay);
            var linkedToken = linkedCts.Token;

            var newChannel = Channel.CreateUnbounded<byte[]>(
                new UnboundedChannelOptions{
                    SingleReader = true,
                    SingleWriter = true,
                }
            );
            var newChannelByteStream = newChannel.Reader.ReadAllAsync(linkedToken);
            var audio = new AudioSource(newChannelByteStream, TimeSpan.Zero, AudioSourceLog, linkedToken);
            var newSegment = new OpenAudioSegment(
                segmentIndex1, audioRecord, audio,
                author, languages,
                OpenAudioSegmentLog);

            _ = BackgroundTask.Run(async () => {
                try {
                    await audio.WhenFormatAvailable.WithFakeCancellation(linkedToken).ConfigureAwait(false);
                    await audio.WhenDurationAvailable.WithFakeCancellation(linkedToken).ConfigureAwait(false);
                    // We don't want to wait for too long here to finalize the entry,
                    // + it should actually come almost instantly
                    await newSegment.AudibleDurationTask
                        .WithTimeout(AudibleDurationWaitDelay, linkedToken)
                        .ConfigureAwait(false);
                    newSegment.Close(audio.Duration);
                }
                catch (Exception e) {
                    // We're ok to close with OperationCancelledException here,
                    // but notice that most likely this won't happen, because the
                    // cancellation here is delayed, and the code "feeding" the
                    // segment with the data will gracefully close it on its
                    // own cancellation.
                    newSegment.TryClose(e);
                    throw;
                }
                finally {
                    linkedCts.Dispose();
                }
            }, Log, $"{nameof(NewAudioSegment)} processing failed", CancellationToken.None);

            await segments.WriteAsync(newSegment, cancellationToken).ConfigureAwait(false);
            return (newSegment, newChannel);
        }
    }
}
