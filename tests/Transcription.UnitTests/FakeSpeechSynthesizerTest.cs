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

    private static IServiceProvider CreateServices()
        => new ServiceCollection()
            .AddSingleton(MomentClockSet.Default)
            .BuildServiceProvider();
}
