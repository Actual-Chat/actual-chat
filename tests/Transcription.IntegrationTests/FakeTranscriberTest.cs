using ActualChat.Audio;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class FakeTranscriberTest(ITestOutputHelper @out, ILogger<FakeTranscriberTest> log)
    : TranscriberTestBase(@out, log)
{
    private const double MinWordsPerSecond = 1.5;
    private const double MaxWordsPerSecond = 5.0;
    private const double MeanWordsPerSecond = 3.0;

    [Theory]
    [InlineData("large-file.webm", "en-US")]
    [InlineData("large-file.webm", "ru-RU")]
    public async Task TranscribeProducesPacedTemplateText(string fileName, string languageId)
    {
        // arrange
        var services = CreateServices();
        var transcriber = new FakeTranscriber(services);
        var language = Language.Parse(languageId);
        var options = new TranscriptionOptions { Language = language };
        var audio = await GetAudio(fileName);

        // act
        var transcripts = await transcriber.Transcribe("test-" + languageId, audio, options).ToListAsync();
        WriteLine($"Got {transcripts.Count} transcripts; last: {transcripts[^1]}");

        // assert
        transcripts.Should().NotBeEmpty();
        var final = transcripts[^1];
        final.IsStable.Should().BeTrue();
        final.Languages.Should().Equal(language);

        var audioSeconds = audio.Duration.TotalSeconds;
        audioSeconds.Should().BeGreaterThan(2,
            "the test relies on a sufficiently long audio sample");

        // Word count is bounded by the WPS range; stay generous to absorb
        // randomness on short clips.
        var actualWordCount = final.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var minWords = (int)Math.Floor(audioSeconds * MinWordsPerSecond) - 1;
        var maxWords = (int)Math.Ceiling(audioSeconds * MaxWordsPerSecond) + 1;
        actualWordCount.Should().BeInRange(Math.Max(0, minWords), maxWords);

        // Mean WPS over the whole sample should land near the target.
        var meanWps = actualWordCount / audioSeconds;
        meanWps.Should().BeInRange(MinWordsPerSecond, MaxWordsPerSecond);

        // Time map endpoint should match audio duration.
        final.TimeRange.End.Should().BeApproximately((float)audioSeconds, 0.05f);

        // Template selected by language family.
        if (languageId.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
            final.Text.Should().MatchRegex(@"[\u0400-\u04FF]", "Russian template should contain Cyrillic letters");
        else
            final.Text.Should().MatchRegex("[a-zA-Z]", "English template should contain Latin letters");

        // Intermediate transcripts grow monotonically and are unstable.
        var lastLen = 0;
        for (var i = 0; i < transcripts.Count - 1; i++) {
            transcripts[i].IsStable.Should().BeFalse();
            transcripts[i].Length.Should().BeGreaterThanOrEqualTo(lastLen);
            lastLen = transcripts[i].Length;
        }
    }

    [Fact]
    public void SampledMeanWordsPerSecondShouldBeNearThree()
    {
        // arrange — drive the same triangular distribution by transcribing many short streams.
        var services = CreateServices();
        var transcriber = new FakeTranscriber(services);

        // act: average across many seeds & a fixed synthetic 60 s audio source.
        var samples = new List<double>();
        for (var i = 0; i < 50; i++) {
            var audio = new SyntheticAudioSource(TimeSpan.FromSeconds(60), Log);
            var options = new TranscriptionOptions { Language = Languages.English };
            var transcripts = transcriber
                .Transcribe($"seed-{i}", audio, options)
                .ToBlockingEnumerable()
                .ToList();
            var final = transcripts[^1];
            var words = final.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            samples.Add(words / 60.0);
        }

        // assert
        var mean = samples.Average();
        WriteLine($"Mean WPS over {samples.Count} runs: {mean:F2} (range [{samples.Min():F2}, {samples.Max():F2}])");
        mean.Should().BeApproximately(MeanWordsPerSecond, 0.15);
    }

    private IServiceProvider CreateServices()
        => new ServiceCollection()
            .AddSingleton(MomentClockSet.Default)
            .AddTestLogging(Out)
            .BuildServiceProvider();

    // Nested types

    private sealed class SyntheticAudioSource : AudioSource
    {
        public SyntheticAudioSource(TimeSpan duration, ILogger log)
            : base(default, DefaultFormat, BuildFrames(duration), TimeSpan.Zero, log, CancellationToken.None)
        { }

        private static async IAsyncEnumerable<AudioFrame> BuildFrames(TimeSpan duration)
        {
            var frame = Constants.Audio.OpusFrameDuration;
            var n = (int)Math.Ceiling(duration.TotalMilliseconds / frame.TotalMilliseconds);
            for (var i = 0; i < n; i++) {
                yield return new AudioFrame {
                    Offset = i * frame,
                    Duration = frame,
                    Data = ReadOnlyMemory<byte>.Empty,
                };
            }
            await Task.CompletedTask;
        }
    }
}
