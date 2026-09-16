using ActualChat.Audio;

namespace ActualChat.Transcription.UnitTests;

public sealed class FakeSpeechSynthesizerTest
{
    [Fact]
    public async Task FakeShouldSpeakSilenceProportionalToText()
    {
        // arrange
        var synthesizer = new FakeSpeechSynthesizer(CreateServices());
        var text = Channel.CreateUnbounded<string>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        text.Writer.TryWrite("12345678");
        text.Writer.Complete();

        // act
        await synthesizer.Synthesize(
            "test", text.Reader, new SpeechSynthesisOptions(Languages.English), frames.Writer);
        var result = await frames.Reader.ReadAllAsync().ToListAsync();

        // assert
        result.Should().HaveCount(2, "an 8-char chunk speaks two 20ms frames of silence");
        result[0].Offset.Should().Be(TimeSpan.Zero);
        result[1].Offset.Should().Be(Constants.Audio.OpusFrameDuration);
    }

    [Fact]
    public async Task FakeShouldPropagateATextFault()
    {
        // arrange
        var synthesizer = new FakeSpeechSynthesizer(CreateServices());
        var text = Channel.CreateUnbounded<string>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        text.Writer.Complete(new InvalidOperationException("boom"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // act
        var act = () => synthesizer.Synthesize(
            "test", text.Reader, new SpeechSynthesisOptions(Languages.English), frames.Writer, cts.Token);

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*boom*");
        var readAll = () => frames.Reader.ReadAllAsync().ToListAsync().AsTask();
        await readAll.Should().ThrowAsync<InvalidOperationException>("the output carries the same error");
    }

    [Fact]
    public async Task OneShotSynthesisReturnsAWholeAudioSource()
    {
        // arrange
        var synthesizer = new FakeSpeechSynthesizer(CreateServices());

        // act
        var audio = await synthesizer.Synthesize(
            "Hello there, how are you?", new SpeechSynthesisOptions(Languages.English));
        var frames = await audio.GetFrames(CancellationToken.None).ToListAsync();

        // assert
        frames.Should().HaveCount(6, "one 20ms frame per four characters");
        await audio.WhenDurationAvailable;
        audio.Duration.Should().Be(TimeSpan.FromMilliseconds(120));
    }

    [Fact(Timeout = 30_000)]
    public async Task FakeShouldReportOneStreamWithAudio()
    {
        // arrange
        var synthesizer = new FakeSpeechSynthesizer(CreateServices());
        var listener = new RecordingListener();
        var text = Channel.CreateUnbounded<string>();
        var output = Channel.CreateUnbounded<AudioFrame>();
        text.Writer.TryWrite("Hello, world.");
        text.Writer.TryComplete();
        var options = new SpeechSynthesisOptions(Languages.English) { Listener = listener };

        // act
        await synthesizer.Synthesize("s1", text.Reader, options, output.Writer, CancellationToken.None);

        // assert
        listener.StreamsOpened.Should().Be(1);
        listener.AudioStarts.Should().Be(1);
    }

    private static IServiceProvider CreateServices()
        => new ServiceCollection()
            .AddSingleton(MomentClockSet.Default)
            .BuildServiceProvider();

    private sealed class RecordingListener : ISpeechSynthesisListener
    {
        public int StreamsOpened;
        public int AudioStarts;

        public void OnStreamOpened() => StreamsOpened++;
        public void OnAudioStarted() => AudioStarts++;
    }
}
