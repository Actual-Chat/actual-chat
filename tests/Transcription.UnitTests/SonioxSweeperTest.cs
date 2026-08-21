using ActualChat.Module;

namespace ActualChat.Transcription.UnitTests;

public sealed class SonioxSweeperTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateTimeOffset Old = Now - TimeSpan.FromHours(1);

    [Fact]
    public async Task SweepDeletesOldTranscriptionsWithTheirFiles()
    {
        // arrange
        var api = new SonioxApiMock()
            .AddTranscription("t1", Old, fileId: "f1")
            .AddFile("f1", Old);

        // act
        var deleted = await NewSweeper(api).Sweep(CancellationToken.None);

        // assert - one delete, and it's the transcription's, which takes its file with it
        api.DeleteAttempts.Should().Equal("t1");
        deleted.Should().Be(1);
        api.TranscriptionIds.Should().BeEmpty();
        api.FileIds.Should().BeEmpty();
    }

    [Fact]
    public async Task SweepKeepsRecentAndInFlightOnes()
    {
        // arrange
        var api = new SonioxApiMock()
            .AddTranscription("t1", Now, fileId: "f1")
            .AddTranscription("t2", Old, "processing")
            .AddFile("f1", Now);

        // act
        var deleted = await NewSweeper(api).Sweep(CancellationToken.None);

        // assert - never even attempted: Soniox answers a running transcription's delete with 409
        deleted.Should().Be(0);
        api.DeleteAttempts.Should().BeEmpty();
        api.TranscriptionIds.Should().BeEquivalentTo("t1", "t2");
        api.FileIds.Should().BeEquivalentTo("f1");
    }

    [Fact]
    public async Task SweepDeletesFilesLeftByAFailedCreate()
    {
        // arrange - what the cap itself produces: the upload succeeds, the create right after 429s
        var api = new SonioxApiMock()
            .AddFile("f1", Old)
            .AddFile("f2", Now);

        // act
        var deleted = await NewSweeper(api).Sweep(CancellationToken.None);

        // assert
        deleted.Should().Be(1);
        api.FileIds.Should().BeEquivalentTo("f2");
    }

    [Fact]
    public async Task SweepStopsAtMaxDeletesAcrossBothPasses()
    {
        // arrange
        var api = new SonioxApiMock()
            .AddTranscription("t1", Old)
            .AddTranscription("t2", Old)
            .AddFile("f1", Old)
            .AddFile("f2", Old);

        // act
        var settings = new SonioxSweeper.Options { MaxDeletesPerSweep = 3, DeleteDelay = TimeSpan.Zero };
        var deleted = await NewSweeper(api, settings).Sweep(CancellationToken.None);

        // assert - the budget is what's left of it by the time the file pass runs
        deleted.Should().Be(3);
        api.TranscriptionIds.Should().BeEmpty();
        api.FileIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task SweepFollowsThePageCursor()
    {
        // arrange
        var api = new SonioxApiMock();
        for (var i = 0; i < 25; i++)
            api.AddTranscription($"t{i:00}", Old);

        // act
        var deleted = await NewSweeper(api, new SonioxSweeper.Options { PageSize = 10, DeleteDelay = TimeSpan.Zero })
            .Sweep(CancellationToken.None);

        // assert - and the page size has to reach Soniox, which it doesn't under the wrong parameter
        deleted.Should().Be(25);
        api.TranscriptionIds.Should().BeEmpty();
        api.RequestedPageSizes.Should().OnlyContain(x => x == 10);
    }

    [Fact]
    public async Task SweepWaitsOutARateLimitAndKeepsGoing()
    {
        // arrange - the budget is shared with live transcription, so a 429 says "later", not "no"
        var api = new SonioxApiMock()
            .AddTranscription("t1", Old)
            .AddTranscription("t2", Old);
        api.RateLimitedDeleteCount = 1;
        api.RetryAfter = TimeSpan.FromMilliseconds(10);
        var settings = new SonioxSweeper.Options { DeleteDelay = TimeSpan.Zero };

        // act
        var deleted = await NewSweeper(api, settings).Sweep(CancellationToken.None);

        // assert - the refused one is retried rather than written off, and both still land
        deleted.Should().Be(2);
        api.DeleteAttempts.Should().Equal("t1", "t1", "t2");
        api.TranscriptionIds.Should().BeEmpty();
    }

    [Fact]
    public async Task SweepGivesUpOnAnEndlessRateLimit()
    {
        // arrange
        var api = new SonioxApiMock().AddTranscription("t1", Old);
        api.RateLimitedDeleteCount = int.MaxValue;
        api.RetryAfter = TimeSpan.FromMilliseconds(1);
        var settings = new SonioxSweeper.Options {
            DeleteDelay = TimeSpan.Zero,
            MaxRateLimitRetryCount = 3,
        };

        // act
        var deleted = await NewSweeper(api, settings).Sweep(CancellationToken.None);

        // assert - the id survives for the next sweep rather than stalling this one forever
        deleted.Should().Be(0);
        api.DeleteAttempts.Should().HaveCount(4);
        api.TranscriptionIds.Should().BeEquivalentTo("t1");
    }

    [Fact]
    public async Task SweepPacesItsDeletes()
    {
        // arrange
        var api = new SonioxApiMock()
            .AddTranscription("t1", Old)
            .AddTranscription("t2", Old);
        var settings = new SonioxSweeper.Options { DeleteDelay = TimeSpan.FromMilliseconds(200) };

        // act
        var startedAt = CpuTimestamp.Now;
        var deleted = await NewSweeper(api, settings).Sweep(CancellationToken.None);

        // assert
        deleted.Should().Be(2);
        startedAt.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(300));
    }

    // Private methods

    private SonioxSweeper NewSweeper(SonioxApiMock api, SonioxSweeper.Options? settings = null)
    {
        // The clock is the real one, so every test that isn't about pacing turns it off.
        settings ??= new SonioxSweeper.Options { DeleteDelay = TimeSpan.Zero };
        var services = new ServiceCollection()
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton(new CoreServerSettings { SonioxKey = "test" })
            .AddSingleton<SonioxClient>()
            .AddTestLogging(Out);
        services.AddHttpClient(SonioxClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => api);
        return new SonioxSweeper(settings, services.BuildServiceProvider());
    }
}
