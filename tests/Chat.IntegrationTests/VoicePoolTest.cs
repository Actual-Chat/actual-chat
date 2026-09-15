using ActualChat.Blobs;
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
    private IBlobStorage Blobs =>
        field ??= AppHost.Services.GetRequiredService<IBlobStorages>()[BlobScope.AudioRecord];

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
        record.SampleHash.Should().Be(VoiceSampleBuilder.HashOf(speaker.Entry.Id), "the record is keyed by the sample");
        var voice = await Soniox.Get(voiceId!, ct);
        voice.Should().NotBeNull("the clone exists at Soniox");
        voice!.Name.Should().Be(Pool.NameOf(speaker.Account.Id, record.SampleHash));
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
        await SetSettings(Tester, x => x with { IsOwnVoiceEnabled = true, OwnVoiceSampleEntryId = newEntry.Id });
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
        record.SampleHash.Should().Be(VoiceSampleBuilder.HashOf(newEntry.Id));
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
        record.SampleHash.Should().Be(VoiceSampleBuilder.HashOf(speaker.Entry.Id), "the sample is still valid");
    }

    [Fact(Timeout = 120_000)]
    public async Task SweepShouldReconcileWithSoniox()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var voiceId = await Pool.Acquire(speaker.Account.Id, ct);
        var orphan = await Soniox.Create(Pool.NamePrefix + "orphan-abc", Stream.Null, ct);
        var otherEnv = await Soniox.Create("voxt-prod-orphan-abc", Stream.Null, ct);
        var foreign = await Soniox.Create("someone-elses-voice", Stream.Null, ct);
        await Soniox.Delete(voiceId!, ct);
        try {
            // act
            await Sweeper.SweepOnce(ct);

            // assert
            Pool.NamePrefix.Should().Be("voxt-test-", "a test host names its clones after its environment");
            (await Soniox.Get(orphan.Id, ct)).Should().BeNull(
                "a voice under our prefix that no record points at is a leftover");
            (await Soniox.Get(otherEnv.Id, ct)).Should().NotBeNull("another environment's clones are its own business");
            (await Soniox.Get(foreign.Id, ct)).Should().NotBeNull("only voxt-* voices are ours to delete");
            var record = await UserVoices.Get(speaker.Account.Id, ct);
            record!.Status.Should().Be(UserVoiceStatus.None, "the record's clone is gone at Soniox");
            record.SonioxVoiceId.Should().BeEmpty();
        }
        finally {
            await Soniox.Delete(otherEnv.Id, ct);
            await Soniox.Delete(foreign.Id, ct);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task FailedSampleShouldKeepAReadyClone()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var voiceId = await Pool.Acquire(speaker.Account.Id, ct);
        var record = await UserVoices.Get(speaker.Account.Id, ct);
        var createCount = Soniox.CreateCount;
        var othersEntry = await RecordSomeoneElsesEntry();
        await SetSettings(Tester, x => x with { OwnVoiceSampleEntryId = othersEntry.Id });

        // act
        var afterFailure = await Pool.Acquire(speaker.Account.Id, ct);
        var again = await Pool.Acquire(speaker.Account.Id, ct);

        // assert
        voiceId.Should().NotBeNullOrEmpty();
        afterFailure.Should().BeNull("no sample means no clone this utterance");
        again.Should().BeNull();
        Soniox.CreateCount.Should().Be(createCount);
        (await Soniox.Get(voiceId!, ct)).Should().NotBeNull("the clone made from the earlier sample is kept");
        var current = await UserVoices.Get(speaker.Account.Id, ct);
        current.Should().BeEquivalentTo(record, "a sample failure leaves a Ready record untouched");
    }

    [Fact(Timeout = 120_000)]
    public async Task AReadyCloneShouldOutliveItsSampleEntry()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var voiceId = await Pool.Acquire(speaker.Account.Id, ct);
        var createCount = Soniox.CreateCount;
        var mediaId = speaker.Entry.Audio!.MediaId!;
        await Commander.Call(new MediaBackend_Change(mediaId, null, Change.Remove<MediaFull>()), ct);

        // act
        var again = await Pool.Acquire(speaker.Account.Id, ct);

        // assert
        voiceId.Should().NotBeNullOrEmpty();
        again.Should().Be(voiceId, "the clone is keyed by the sample's hash, which hasn't changed");
        Soniox.CreateCount.Should().Be(createCount, "nothing is rebuilt while the hash matches");
    }

    [Fact(Timeout = 120_000)]
    public async Task SomeoneElsesEntryShouldNeverBeCloned()
    {
        // arrange - the setting is client-writable and entry media ids are visible to every member
        var account = await Tester.SignInAsUniqueAlice();
        var othersEntry = await RecordSomeoneElsesEntry();
        await SetSettings(Tester, x => x with { IsOwnVoiceEnabled = true, OwnVoiceSampleEntryId = othersEntry.Id });
        var ct = CancellationToken.None;
        var createCount = Soniox.CreateCount;
        var blobId = VoiceSampleBuilder.BlobIdOf(account.Id, VoiceSampleBuilder.HashOf(othersEntry.Id));

        // act
        var voiceId = await Pool.Acquire(account.Id, ct);

        // assert
        voiceId.Should().BeNull("another user's recording is no sample of this one's voice");
        Soniox.CreateCount.Should().Be(createCount, "nothing reaches Soniox");
        (await Blobs.Exists(blobId, ct)).Should().BeFalse("no sample blob is written");
        var record = await UserVoices.Get(account.Id, ct);
        (record?.Status ?? UserVoiceStatus.None).Should().Be(UserVoiceStatus.None, "a missing sample isn't a failure");
    }

    [Fact(Timeout = 120_000)]
    public async Task NameCollisionShouldReplaceTheOrphan()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var name = Pool.NameOf(speaker.Account.Id, VoiceSampleBuilder.HashOf(speaker.Entry.Id));
        var orphan = await Soniox.Create(name, Stream.Null, ct);
        var createCount = Soniox.CreateCount;

        // act
        var voiceId = await Pool.Acquire(speaker.Account.Id, ct);

        // assert
        voiceId.Should().NotBeNullOrEmpty("a same-named leftover is replaced, not a reason to fail");
        voiceId.Should().NotBe(orphan.Id);
        (await Soniox.Get(orphan.Id, ct)).Should().BeNull("the orphan is deleted before the retry");
        Soniox.CreateCount.Should().Be(createCount + 2, "the rejected attempt and the retry");
        var record = await UserVoices.Get(speaker.Account.Id, ct);
        record!.Status.Should().Be(UserVoiceStatus.Ready);
        record.SonioxVoiceId.Should().Be(voiceId);
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
        var entry = await tester.OptInOwnVoice(chatId, Languages.Russian, EntryFrameCount);

        return new Speaker(account, chatId, entry);
    }

    private async Task<ChatEntry> RecordSomeoneElsesEntry()
    {
        await using var otherTester = AppHost.NewWebClientTester(Out);
        await otherTester.SignInAsUniqueBob();
        var (chatId, _) = await otherTester.CreateChat(false);
        return await otherTester.RecordVoiceEntry(chatId, Languages.Russian, frameCount: EntryFrameCount);
    }

    private Task SetSettings(WebClientTester tester, Func<UserLanguageSettings, UserLanguageSettings> update)
        => AppHost.Services.UserSettingsUI(tester.Session)
            .UserLanguageSettings()
            .Update(update, CancellationToken.None);

    // Nested types

    private sealed record Speaker(AccountFull Account, ChatId ChatId, ChatEntry Entry);
}
