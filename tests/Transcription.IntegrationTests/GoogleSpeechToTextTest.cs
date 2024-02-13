using ActualChat.Audio;
using ActualChat.IO;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V2;
using Google.Protobuf;
using ActualLab.IO;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class GoogleSpeechToTextTest(ILogger log, ITestOutputHelper @out)
{
    private ITestOutputHelper Out { get; } = @out;
    private ILogger Log { get; } = log;


    [Theory(Skip = "For manual runs only")]
    [InlineData("0004-AK.webm", false)]
    [InlineData("0004-AK.webm", true)]
    [InlineData("0004-AK.opus", false)]
    [InlineData("0004-AK.opus", true)]
    [InlineData("0004-AK-recoded.opus", false)]
    [InlineData("0004-AK-recoded.opus", true)]
    [InlineData("0004-AK-recoded.webm", false)]
    [InlineData("0004-AK-recoded.webm", true)]
    // [InlineData("0004-AK-recoded.flac", false)]
    // [InlineData("0004-AK-recoded.flac", true)]
    // [InlineData("0004-AK-recoded.wav", false)]
    // [InlineData("0004-AK-recoded.wav", true)]
    [InlineData("large-file.webm", true)]
    public async Task MeasureResponseTime(string fileName, bool withDelay)
    {
        // TODO(AK): try to disable Http/3 for google speech-to-text only instead of global toggle!
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);
        const string recognizer = "projects/784581221205/locations/global/recognizers/r-dev-tst-ru-ru";
        var byteStream = GetAudioFilePath(fileName).ReadByteStream(128, CancellationToken.None);
        var memoized = byteStream.Memoize();
        var resultStream = memoized.Replay();
        if (withDelay) {
            var i = 0;
            resultStream = memoized.Replay()
                .SelectAwait(async chunk => {
                    if (i++ > 69)
                        await Task.Delay(20);
                    return chunk;
                });
        }

        var builder = new SpeechClientBuilder();
        var speechClient = await builder.BuildAsync();
        var recognizeRequests = speechClient
            .StreamingRecognize(
                // CallSettings.FromCancellationToken(CancellationToken.None),
                CallSettings.FromExpiration(Expiration.FromTimeout(TimeSpan.FromMinutes(10))),
                new BidirectionalStreamingSettings(1));
        var streamingRecognitionConfig = new StreamingRecognitionConfig {
            Config = new RecognitionConfig {
                AutoDecodingConfig = new AutoDetectDecodingConfig()
            }, // Use recognizer' settings
            StreamingFeatures = new StreamingRecognitionFeatures {
                InterimResults = true,
                // TODO(AK): test google VAD events - probably it might be useful
                // VoiceActivityTimeout =
                // EnableVoiceActivityEvents =
            },
        };

        await recognizeRequests.WriteAsync(new StreamingRecognizeRequest {
            StreamingConfig = streamingRecognitionConfig,
            Recognizer = recognizer,
        });
        await using var recognizeResponses = recognizeRequests.GetResponseStream();
        await Task.Delay(500);
        // await Task.Delay(20000);

        _ = BackgroundTask.Run(() => PushAudio(resultStream, recognizeRequests, streamingRecognitionConfig),
            Log,
            "Error");
        var startedAt = CpuTimestamp.Now;
        // var firstResponse = await recognizeResponses.FirstAsync();
        var first = false;
        await foreach (var streamingRecognizeResponse in recognizeResponses) {
            if (!first) {
                first = true;
                Out.WriteLine($"First transcription received in: {startedAt}");
                Out.WriteLine(streamingRecognizeResponse.ToString());
            }

            Out.WriteLine(streamingRecognizeResponse.ToString());
        }
    }

    private async Task PushAudio(
        IAsyncEnumerable<byte[]> byteStream,
        SpeechClient.StreamingRecognizeStream recognizeRequests,
        StreamingRecognitionConfig streamingRecognitionConfig)
    {
        try {
            await foreach (var chunk in byteStream.ConfigureAwait(false)) {
                var request = new StreamingRecognizeRequest {
                    StreamingConfig = streamingRecognitionConfig,
                    Audio = ByteString.CopyFrom(chunk),
                };
                await recognizeRequests.WriteAsync(request).ConfigureAwait(false);
            }
        }
        finally {
            await recognizeRequests.WriteCompleteAsync().ConfigureAwait(false);
        }
    }

    private async Task<AudioSource> GetAudio(FilePath fileName, bool? webMStream = null, bool withDelay = false)
    {
        var byteStream = GetAudioFilePath(fileName).ReadByteStream(1024, CancellationToken.None);
        var isWebMStream = webMStream ?? fileName.Extension == ".webm";
        var converter = isWebMStream
            ? (IAudioStreamConverter)new WebMStreamConverter(MomentClockSet.Default, Log)
            : new ActualOpusStreamConverter(MomentClockSet.Default, Log);
        var audio = await converter.FromByteStream(byteStream, CancellationToken.None);
        if (!withDelay)
            return audio;

        var delayedFrames = audio.GetFrames(CancellationToken.None)
            .SelectAwait(async f => {
                await Task.Delay(20);
                return f;
            });
        var delayedAudio = new AudioSource(
            MomentClockSet.Default.SystemClock.Now,
            audio.Format,
            delayedFrames,
            TimeSpan.Zero,
            Log,
            CancellationToken.None);

        return delayedAudio;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
