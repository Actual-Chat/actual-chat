using ActualChat.Media;
using ActualChat.Streaming.Module;
using ActualChat.Streaming.Services;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public sealed class VoicePoolTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    // 20 ms frames, so 600 of them make a 12 s entry - an explicit sample has no minimum length
    private const int EntryFrameCount = 600;
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private VoicePool Pool => field ??= AppHost.Services.GetRequiredService<VoicePool>();
    private VoicePoolSweeper Sweeper => field ??= AppHost.Services.GetRequiredService<VoicePoolSweeper>();
    private FakeSonioxVoices Soniox => field ??= AppHost.Services.GetRequiredService<FakeSonioxVoices>();
    private IUserVoicesBackend UserVoices => field ??= AppHost.Services.GetRequiredService<IUserVoicesBackend>();
    private StreamingSettings Settings => field ??= AppHost.Services.GetRequiredService<StreamingSettings>();

    [Fact(Timeout = 120_000)]
    public async Task AcquireShouldCreateACloneOnceAndReuseIt()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var createCount = Soniox.CreateCount;
        var ct = CancellationToken.None;

        // act
        var voiceId = await Pool.Acquire(speaker.Account.Id, ct);
        var again = await Pool.Acquire(speaker.Account.Id, ct);

        // assert
        voiceId.Should().NotBeNullOrEmpty("an opted-in speaker with a sample gets a clone");
        again.Should().Be(voiceId, "a ready clone is reused");
        Soniox.CreateCount.Should().Be(createCount + 1, "the clone is created once per sample");
        var record = await UserVoices.Get(speaker.Account.Id, ct);
        record.Should().NotBeNull();
        record!.Status.Should().Be(UserVoiceStatus.Ready);
        record.SonioxVoiceId.Should().Be(voiceId);
        record.SampleHash.Should().Be(VoiceSampleBuilder.HashOf(speaker.MediaId), "the record is keyed by the sample");
        var voice = await Soniox.Get(voiceId!, ct);
        voice.Should().NotBeNull("the clone exists at Soniox");
        voice!.Name.Should().Be(VoicePool.NameOf(speaker.Account.Id, record.SampleHash));
        Pool.InFlightCount.Should().Be(0, "nothing stays in flight once the result is handed out");
    }

    [Fact(Timeout = 120_000)]
    public async Task ChangedSampleShouldReplaceTheClone()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var oldVoiceId = await Pool.Acquire(speaker.Account.Id, ct);
        var newEntry = await Tester.RecordVoiceEntry(speaker.ChatId, Languages.Russian, frameCount: EntryFrameCount);
        var newMediaId = newEntry.Audio!.MediaId!;
        await SetSettings(Tester, x => x with { IsOwnVoiceEnabled = true, OwnVoiceSampleMediaId = newMediaId });
        var createCount = Soniox.CreateCount;

        // act
        var newVoiceId = await Pool.Acquire(speaker.Account.Id, ct);

        // assert
        newVoiceId.Should().NotBeNullOrEmpty();
        newVoiceId.Should().NotBe(oldVoiceId, "a new sample means a new clone");
        Soniox.CreateCount.Should().Be(createCount + 1);
        (await Soniox.Get(oldVoiceId!, ct)).Should().BeNull("the clone of the old sample is deleted");
        (await Soniox.Get(newVoiceId!, ct)).Should().NotBeNull();
        var record = await UserVoices.Get(speaker.Account.Id, ct);
        record!.Status.Should().Be(UserVoiceStatus.Ready);
        record.SonioxVoiceId.Should().Be(newVoiceId);
        record.SampleHash.Should().Be(VoiceSampleBuilder.HashOf(newMediaId));
    }

    [Fact(Timeout = 120_000)]
    public async Task FullPoolShouldGiveNull()
    {
        // arrange
        var first = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var firstVoiceId = await Pool.Acquire(first.Account.Id, ct);
        await using var secondTester = AppHost.NewWebClientTester(Out);
        var second = await SignInWithSample(secondTester);
        var activeCount = (await UserVoices.ListActive(ct)).Count;
        var createCount = Soniox.CreateCount;
        Settings.SonioxVoiceQuota = activeCount;
        try {
            // act
            var secondVoiceId = await Pool.Acquire(second.Account.Id, ct);

            // assert
            firstVoiceId.Should().NotBeNullOrEmpty();
            secondVoiceId.Should().BeNull("the pool is full, so the speaker keeps the stock voice");
            Soniox.CreateCount.Should().Be(createCount, "a full pool never calls Soniox");
            var record = await UserVoices.Get(second.Account.Id, ct);
            (record?.Status ?? UserVoiceStatus.None).Should().Be(UserVoiceStatus.None,
                "a full pool isn't a failure of this speaker's clone");
        }
        finally {
            Settings.SonioxVoiceQuota = null;
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task FailedCreateShouldCoolDown()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var createCount = Soniox.CreateCount;
        Soniox.FailCreate = true;
        try {
            // act
            var voiceId = await Pool.Acquire(speaker.Account.Id, ct);
            Soniox.FailCreate = false;
            var retried = await Pool.Acquire(speaker.Account.Id, ct);

            // assert
            voiceId.Should().BeNull("a failed clone means the stock voice");
            retried.Should().BeNull("the clone isn't retried within the cool-down");
            Soniox.CreateCount.Should().Be(createCount + 1, "only the failed attempt reached Soniox");
            var record = await UserVoices.Get(speaker.Account.Id, ct);
            record!.Status.Should().Be(UserVoiceStatus.Failed);
            record.SonioxVoiceId.Should().BeEmpty();
            record.FailedUntil.Should().NotBeNull();
            record.FailedUntil!.Value.Should().BeGreaterThan(Clocks.SystemClock.Now,
                "the cool-down is still running");
        }
        finally {
            Soniox.FailCreate = false;
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task SweepShouldDropAnIdleClone()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var voiceId = await Pool.Acquire(speaker.Account.Id, ct);
        var record = await UserVoices.Get(speaker.Account.Id, ct);
        var idleSince = Clocks.SystemClock.Now - Constants.Audio.VoiceCloneIdleTimeout - TimeSpan.FromMinutes(1);
        var idleDiff = new UserVoiceDiff { LastUsedAt = idleSince };
        await Commander.Call(
            new UserVoicesBackend_Change(speaker.Account.Id, record!.Version, Change.Update(idleDiff)), ct);

        // act
        await Sweeper.SweepOnce(ct);

        // assert
        (await Soniox.Get(voiceId!, ct)).Should().BeNull("an idle clone is deleted at Soniox");
        record = await UserVoices.Get(speaker.Account.Id, ct);
        record!.Status.Should().Be(UserVoiceStatus.None);
        record.SonioxVoiceId.Should().BeEmpty();
        record.SampleHash.Should().Be(VoiceSampleBuilder.HashOf(speaker.MediaId), "the sample is still valid");
    }

    [Fact(Timeout = 120_000)]
    public async Task SweepShouldReconcileWithSoniox()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var voiceId = await Pool.Acquire(speaker.Account.Id, ct);
        var orphan = await Soniox.Create("voxt-orphan-abc", Stream.Null, ct);
        var foreign = await Soniox.Create("someone-elses-voice", Stream.Null, ct);
        await Soniox.Delete(voiceId!, ct);
        try {
            // act
            await Sweeper.SweepOnce(ct);

            // assert
            (await Soniox.Get(orphan.Id, ct)).Should().BeNull("a voxt-* voice no record points at is a leftover");
            (await Soniox.Get(foreign.Id, ct)).Should().NotBeNull("only voxt-* voices are ours to delete");
            var record = await UserVoices.Get(speaker.Account.Id, ct);
            record!.Status.Should().Be(UserVoiceStatus.None, "the record's clone is gone at Soniox");
            record.SonioxVoiceId.Should().BeEmpty();
        }
        finally {
            await Soniox.Delete(foreign.Id, ct);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task OptOutShouldDropTheClone()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var voiceId = await Pool.Acquire(speaker.Account.Id, ct);
        await SetSettings(Tester, x => x with { IsOwnVoiceEnabled = false });
        var createCount = Soniox.CreateCount;

        // act
        var afterOptOut = await Pool.Acquire(speaker.Account.Id, ct);

        // assert
        voiceId.Should().NotBeNullOrEmpty();
        afterOptOut.Should().BeNull("an opted-out speaker is dubbed with the stock voice");
        Soniox.CreateCount.Should().Be(createCount);
        (await Soniox.Get(voiceId!, ct)).Should().BeNull("opting out deletes the clone");
        var record = await UserVoices.Get(speaker.Account.Id, ct);
        record!.Status.Should().Be(UserVoiceStatus.None);
        record.SonioxVoiceId.Should().BeEmpty();
    }

    // Private methods

    private async Task<Speaker> SignInWithSample(WebClientTester tester)
    {
        var account = await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(false);
        // RecordVoiceEntry replaces the language settings, so the opt-in goes after it
        var entry = await tester.RecordVoiceEntry(chatId, Languages.Russian, frameCount: EntryFrameCount);
        var mediaId = entry.Audio!.MediaId!;
        await SetSettings(tester, x => x with { IsOwnVoiceEnabled = true, OwnVoiceSampleMediaId = mediaId });

        return new Speaker(account, chatId, mediaId);
    }

    private Task SetSettings(WebClientTester tester, Func<UserLanguageSettings, UserLanguageSettings> update)
        => AppHost.Services.UserSettingsUI(tester.Session)
            .UserLanguageSettings()
            .Update(update, CancellationToken.None);

    // Nested types

    private sealed record Speaker(AccountFull Account, ChatId ChatId, MediaId MediaId);
}
