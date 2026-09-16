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
        var pcm = Channel.CreateUnbounded<byte[]>();
        text.Writer.TryWrite("12345678");
        text.Writer.Complete();

        // act
        await synthesizer.Synthesize(
            "test", text.Reader, new SpeechSynthesisOptions(Languages.English), pcm.Writer);
        var result = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        var byteCount = result.Sum(x => x.Length);
        (byteCount / OpusFramePump.FrameByteLength).Should().Be(2, "an 8-char chunk speaks two 20ms frames of silence");
        (byteCount % OpusFramePump.FrameByteLength).Should().Be(0, "the fake speaks whole frames");
    }

    [Fact]
    public async Task FakeShouldPropagateATextFault()
    {
        // arrange
        var synthesizer = new FakeSpeechSynthesizer(CreateServices());
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        text.Writer.Complete(new InvalidOperationException("boom"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // act
        var act = () => synthesizer.Synthesize(
            "test", text.Reader, new SpeechSynthesisOptions(Languages.English), pcm.Writer, cts.Token);

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*boom*");
        var readAll = () => pcm.Reader.ReadAllAsync().ToListAsync().AsTask();
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
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        var listener = new RecordingListener(pcm.Reader);
        text.Writer.TryWrite("Hello, world.");
        text.Writer.TryComplete();
        var options = new SpeechSynthesisOptions(Languages.English) { Listener = listener };

        // act
        await synthesizer.Synthesize("s1", text.Reader, options, pcm.Writer, CancellationToken.None);

        // assert
        listener.StreamsOpened.Should().Be(1);
        listener.AudioStarts.Should().Be(1);
        listener.ChunksAtStreamOpened.Should().Be(0, "the stream opens before the first PCM write");
        listener.ChunksAtAudioStarted.Should().Be(1, "audio starts right after the first PCM write");
    }

    private static IServiceProvider CreateServices()
        => new ServiceCollection()
            .AddSingleton(MomentClockSet.Default)
            .BuildServiceProvider();

    private sealed class RecordingListener(ChannelReader<byte[]> pcm) : ISpeechSynthesisListener
    {
        public int StreamsOpened;
        public int AudioStarts;
        public int ChunksAtStreamOpened = -1;
        public int ChunksAtAudioStarted = -1;

        public void OnStreamOpened()
        {
            StreamsOpened++;
            ChunksAtStreamOpened = pcm.Count;
        }

        public void OnAudioStarted()
        {
            AudioStarts++;
            ChunksAtAudioStarted = pcm.Count;
        }
    }
}
