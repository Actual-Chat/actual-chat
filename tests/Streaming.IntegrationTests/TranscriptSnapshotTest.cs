using System.Numerics;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class TranscriptSnapshotTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact(Timeout = 60_000)]
    public async Task TranscriptSnapshotFollowsThePushedDiffs()
    {
        // arrange
        var services = AppHost.Services;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var streamId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = cts.Token;

        var diffs = Channel.CreateUnbounded<TranscriptDiff>();
        var pushTask = BackgroundTask.Run(
            () => backend.PushTranscript(streamId, new RpcStream<TranscriptDiff>(diffs.Reader.ReadAllAsync(ct)), ct),
            ct);

        var cMerged = await Computed.Capture(() => backend.GetTranscriptSnapshot(streamId, ct), ct);

        // act
        var hello = Text("Hello");
        diffs.Writer.TryWrite(hello - Transcript.Empty);

        // assert
        cMerged = await cMerged.When(x => x is { Text: "Hello" }, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);

        // act - the compute must follow further diffs, not stay pinned to the first fold
        var helloWorld = Text("Hello world");
        diffs.Writer.TryWrite(helloWorld - hello);

        // assert
        await cMerged.When(x => x is { Text: "Hello world" }, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);

        diffs.Writer.Complete();
        await pushTask.SilentAwait(false);
    }

    [Fact(Timeout = 60_000)]
    public async Task TranscriptSnapshotIsNullForAnUnknownStream()
    {
        // arrange
        var services = AppHost.Services;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var streamId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // act
        var merged = await backend.GetTranscriptSnapshot(streamId, cts.Token);

        // assert
        merged.Should().BeNull();
    }

    private static Transcript Text(string text)
        => new(text, LinearMap.Zero.Append(new Vector2(text.Length, text.Length)), []);
}
