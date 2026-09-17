using System.Numerics;
using ActualChat.Transcription;

namespace ActualChat.Streaming.UnitTests;

public class ReadTranslationTest
{
    [Fact(Timeout = 15_000)]
    public async Task ReadShouldWaitForTheTranslationOfTheLastStableIncrement()
    {
        // arrange - the source ends while its last stable increment is still being translated
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var translated = Channel.CreateUnbounded<TranscriptDiff>();
        using var sourceMemoizer = source.Reader.ReadAllAsync(ct).Memoize(ct);
        using var translatedMemoizer = translated.Reader.ReadAllAsync(ct).Memoize(ct);
        var sourceFirst = Stable("Hello world.", 1f);
        var sourceSecond = Stable("Hello world. How are you?", 2f);
        source.Writer.TryWrite(sourceFirst - Transcript.Empty);
        source.Writer.TryWrite(sourceSecond - sourceFirst);
        source.Writer.Complete();
        var translatedFirst = Stable("Привет, мир.", 1f);
        translated.Writer.TryWrite(translatedFirst - Transcript.Empty);
        var folded = Transcript.Empty;
        bool IsTranslationComplete()
            => folded.IsStable
                && folded.TimeRange.End + Transcript.TimeMapEpsilon.Y >= sourceSecond.TimeRange.End;

        // act
        var read = new List<TranscriptDiff>();
        var readTask = Task.Run(async () => {
            await foreach (var diff in AudioStreamingBackend
                               .ReadTranslation(translatedMemoizer, sourceMemoizer, IsTranslationComplete, ct)
                               .ConfigureAwait(false)) {
                folded += diff;
                read.Add(diff);
            }
        }, ct);
        await Task.Delay(500, ct);
        translated.Writer.TryWrite(Stable("Привет, мир. Как дела?", 2f) - translatedFirst);
        await readTask.WaitAsync(ct);

        // assert
        read.Should().HaveCount(2, "the read must not end while the last increment is untranslated");
        folded.Text.Should().Be("Привет, мир. Как дела?");
    }

    [Fact(Timeout = 15_000)]
    public async Task ReadShouldEndOnceTheWholeSourceIsTranslatedEvenIfTheStreamStaysOpen()
    {
        // arrange - the translated stream stays open until the entry is finalized, long after the last diff
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var translated = Channel.CreateUnbounded<TranscriptDiff>();
        using var sourceMemoizer = source.Reader.ReadAllAsync(ct).Memoize(ct);
        using var translatedMemoizer = translated.Reader.ReadAllAsync(ct).Memoize(ct);
        var sourceFinal = Stable("Hello world.", 1f);
        source.Writer.TryWrite(sourceFinal - Transcript.Empty);
        source.Writer.Complete();
        translated.Writer.TryWrite(Stable("Привет, мир.", 1f) - Transcript.Empty);
        var folded = Transcript.Empty;
        bool IsTranslationComplete()
            => folded.IsStable
                && folded.TimeRange.End + Transcript.TimeMapEpsilon.Y >= sourceFinal.TimeRange.End;

        // act
        var read = new List<TranscriptDiff>();
        await foreach (var diff in AudioStreamingBackend
                           .ReadTranslation(translatedMemoizer, sourceMemoizer, IsTranslationComplete, ct)
                           .ConfigureAwait(false)) {
            folded += diff;
            read.Add(diff);
        }

        // assert
        read.Should().ContainSingle();
        translated.Reader.Completion.IsCompleted.Should().BeFalse("the read ended on its own, not with the stream");
    }

    [Fact(Timeout = 15_000)]
    public async Task ReadShouldRecheckAgainstTheSourceThatGrewAfterTheLastTranslation()
    {
        // arrange - the translation caught up with the source, then the source grew and ended
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var translated = Channel.CreateUnbounded<TranscriptDiff>();
        using var sourceMemoizer = source.Reader.ReadAllAsync(ct).Memoize(ct);
        using var translatedMemoizer = translated.Reader.ReadAllAsync(ct).Memoize(ct);
        var sourceFirst = Stable("Hello world.", 1f);
        source.Writer.TryWrite(sourceFirst - Transcript.Empty);
        var translatedFirst = Stable("Привет, мир.", 1f);
        translated.Writer.TryWrite(translatedFirst - Transcript.Empty);
        var folded = Transcript.Empty;
        bool IsTranslationComplete()
            => folded.IsStable
                && folded.TimeRange.End + Transcript.TimeMapEpsilon.Y
                >= sourceMemoizer.FoldBuffered(Transcript.Empty, (t, d) => t + d).Value.TimeRange.End;

        // act
        var read = new List<TranscriptDiff>();
        var readTask = Task.Run(async () => {
            await foreach (var diff in AudioStreamingBackend
                               .ReadTranslation(translatedMemoizer, sourceMemoizer, IsTranslationComplete, ct)
                               .ConfigureAwait(false)) {
                folded += diff;
                read.Add(diff);
            }
        }, ct);
        await Task.Delay(300, ct);
        source.Writer.TryWrite(Stable("Hello world. How are you?", 2f) - sourceFirst);
        source.Writer.Complete();
        await Task.Delay(300, ct);
        translated.Writer.TryWrite(Stable("Привет, мир. Как дела?", 2f) - translatedFirst);
        await readTask.WaitAsync(ct);

        // assert
        read.Should().HaveCount(2, "the source's end must be re-read once it has actually ended");
    }

    private static Transcript Stable(string text, float endTime)
        => new(text, LinearMap.Zero.Append(new Vector2(text.Length, endTime)), []) { IsStable = true };
}
