using System.Numerics;
using ActualChat.Transcription;

namespace ActualChat.Streaming.UnitTests;

// The dub worker folds the source transcript stream for as long as the dub runs, which is past the
// point where ProcessAudio, done with the source, disposes the memoizer it published.

public class SourceTranscriptFoldTest
{
    [Fact(Timeout = 15_000)]
    public async Task TheFoldShouldKeepTheSourceEndAfterTheProducerDisposesTheStream()
    {
        // arrange - the sequence Soniox yields for a one-word utterance, then the producer is done with it
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var memoizer = source.Reader.ReadAllAsync(ct).MemoizeFolding(
            Transcript.Empty,
            AudioStreamingBackend.TranscriptFolder,
            AudioStreamingBackend.TranscriptToDiff,
            ct);
        var unstable = new Transcript("Да", LinearMap.Zero.Append(new Vector2(2, 1f)), [Languages.Russian]);
        var stable = unstable with { IsStable = true };
        source.Writer.TryWrite(unstable - Transcript.Empty);
        source.Writer.TryWrite(stable - unstable);
        source.Writer.TryWrite(stable - stable);
        source.Writer.Complete();
        await memoizer.WhenRunning!.WaitAsync(ct);

        // act
        await memoizer.DisposeAsync();
        var (folded, _) = memoizer.Fold();

        // assert
        folded.Text.Should().Be("Да");
        folded.IsStable.Should().BeTrue();
        folded.TimeRange.End.Should().BeApproximately(stable.TimeRange.End, 0.001f,
            "the dub's IsTranslationComplete, backlog skip and latency lines read the end from this fold");
    }
}
