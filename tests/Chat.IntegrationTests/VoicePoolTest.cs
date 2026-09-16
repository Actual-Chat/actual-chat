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
    public async Task ReadyCloneShouldBeReusedWithoutSoniox()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var voiceId = await Pool.AcquireSettled(speaker.Account.Id, ct);
        var createCount = Soniox.CreateCount;
        var deleteCount = Soniox.DeleteCount;

        // act
        var again = await Pool.Acquire(speaker.Account.Id, ct);

        // assert
        voiceId.Should().NotBeNullOrEmpty("an opted-in speaker with a sample gets a clone");
        again.Should().Be(voiceId, "a ready clone is reused");
        Soniox.CreateCount.Should().Be(createCount, "the clone is created once per sample");
        Soniox.DeleteCount.Should().Be(deleteCount, "a ready clone costs no Soniox call");
        Pool.InFlightCount.Should().Be(0, "a ready clone starts no work");
        var record = await UserVoices.Get(speaker.Account.Id, ct);
        record.Should().NotBeNull();
        record!.Status.Should().Be(UserVoiceStatus.Ready);
        record.SonioxVoiceId.Should().Be(voiceId);
        record.SampleHash.Should().Be(VoiceSampleBuilder.HashOf(speaker.Entry.Id), "the record is keyed by the sample");
        var voice = await Soniox.Get(voiceId!, ct);
        voice.Should().NotBeNull("the clone exists at Soniox");
        voice!.Name.Should().Be(Pool.NameOf(speaker.Account.Id, record.SampleHash));
    }

    [Fact(Timeout = 120_000)]
    public async Task FirstAcquireShouldStartTheCloneAndGiveNullAtOnce()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var createCount = Soniox.CreateCount;
        Soniox.ReadyAfter = TimeSpan.FromSeconds(3);
        try {
            // act
            var startedAt = CpuTimestamp.Now;
            var first = await Pool.Acquire(speaker.Account.Id, ct);
            var elapsed = startedAt.Elapsed;
            var inFlightCount = Pool.InFlightCount;
            await Pool.WhenSettled();
            var next = await Pool.Acquire(speaker.Account.Id, ct);

            // assert
            first.Should().BeNull("the dub that asks first speaks with the stock voice while the clone is made");
            elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "Acquire never waits for the clone");
            inFlightCount.Should().Be(1, "the clone is being made in the background");
            Soniox.CreateCount.Should().Be(createCount + 1, "the first Acquire started the creation");
            next.Should().NotBeNullOrEmpty("the clone serves the next utterance");
            var record = await UserVoices.Get(speaker.Account.Id, ct);
            record!.Status.Should().Be(UserVoiceStatus.Ready);
            record.SonioxVoiceId.Should().Be(next);
            Pool.InFlightCount.Should().Be(0, "nothing stays in flight once the clone is ready");
        }
        finally {
            Soniox.ReadyAfter = TimeSpan.Zero;
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task AcquireWhileCreatingShouldNotStartASecondClone()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var createCount = Soniox.CreateCount;
        Soniox.ReadyAfter = TimeSpan.FromSeconds(3);
        try {
            var first = await Pool.Acquire(speaker.Account.Id, ct);

            // act
            var whileCreating = await Pool.Acquire(speaker.Account.Id, ct);
            var inFlightCount = Pool.InFlightCount;
            await Pool.WhenSettled();

            // assert
            first.Should().BeNull();
            whileCreating.Should().BeNull("a dub during the creation still speaks with the stock voice");
            inFlightCount.Should().Be(1, "the second Acquire joins the creation instead of starting another");
            Soniox.CreateCount.Should().Be(createCount + 1, "the clone is created once");
            (await Pool.Acquire(speaker.Account.Id, ct)).Should().NotBeNullOrEmpty();
        }
        finally {
            Soniox.ReadyAfter = TimeSpan.Zero;
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task ChangedSampleShouldReplaceTheClone()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var oldVoiceId = await Pool.AcquireSettled(speaker.Account.Id, ct);
        var oldBlobId = VoiceSampleBuilder.BlobIdOf(speaker.Account.Id, VoiceSampleBuilder.HashOf(speaker.Entry.Id));
        var newEntry = await Tester.RecordVoiceEntry(speaker.ChatId, Languages.Russian, frameCount: EntryFrameCount);
        await SetSettings(Tester, x => x with { IsOwnVoiceEnabled = true, OwnVoiceSampleEntryId = newEntry.Id });
        var createCount = Soniox.CreateCount;

        // act
        var withStaleClone = await Pool.Acquire(speaker.Account.Id, ct);
        var newVoiceId = await Pool.AcquireSettled(speaker.Account.Id, ct);

        // assert
        withStaleClone.Should().BeNull("a changed sample means a new clone, and the stock voice until it's made");
        newVoiceId.Should().NotBeNullOrEmpty();
        newVoiceId.Should().NotBe(oldVoiceId, "a new sample means a new clone");
        Soniox.CreateCount.Should().Be(createCount + 1);
        (await Soniox.Get(oldVoiceId!, ct)).Should().BeNull("the clone of the old sample is deleted");
        (await Soniox.Get(newVoiceId!, ct)).Should().NotBeNull();
        var record = await UserVoices.Get(speaker.Account.Id, ct);
        record!.Status.Should().Be(UserVoiceStatus.Ready);
        record.SonioxVoiceId.Should().Be(newVoiceId);
        record.SampleHash.Should().Be(VoiceSampleBuilder.HashOf(newEntry.Id));
        (await Blobs.Exists(oldBlobId, ct)).Should().BeFalse("the old sample goes with its clone");
        var newBlobId = VoiceSampleBuilder.BlobIdOf(speaker.Account.Id, record.SampleHash);
        (await Blobs.Exists(newBlobId, ct)).Should().BeTrue("the new sample stays with the new clone");
    }

    [Fact(Timeout = 120_000)]
    public async Task FullPoolShouldGiveNull()
    {
        // arrange
        var first = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var firstVoiceId = await Pool.AcquireSettled(first.Account.Id, ct);
        await using var secondTester = AppHost.NewWebClientTester(Out);
        var second = await SignInWithSample(secondTester);
        var activeCount = (await UserVoices.ListActive(ct)).Count;
        var createCount = Soniox.CreateCount;
        Settings.SonioxVoiceQuota = activeCount;
        try {
            // act
            var secondVoiceId = await Pool.AcquireSettled(second.Account.Id, ct);

            // assert
            firstVoiceId.Should().NotBeNullOrEmpty();
            secondVoiceId.Should().BeNull("the pool is full, so the speaker keeps the stock voice");
            Soniox.CreateCount.Should().Be(createCount, "a full pool never calls Soniox");
            var record = await UserVoices.Get(second.Account.Id, ct);
            (record?.Status ?? UserVoiceStatus.None).Should().Be(UserVoiceStatus.None,
                "a full pool isn't a failure of this speaker's clone");
            var blobId = VoiceSampleBuilder.BlobIdOf(second.Account.Id, VoiceSampleBuilder.HashOf(second.Entry.Id));
            (await Blobs.Exists(blobId, ct)).Should().BeFalse("nothing is built for a full pool");
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
        var blobId = VoiceSampleBuilder.BlobIdOf(speaker.Account.Id, VoiceSampleBuilder.HashOf(speaker.Entry.Id));
        Soniox.FailCreate = true;
        try {
            // act
            var voiceId = await Pool.AcquireSettled(speaker.Account.Id, ct);
            Soniox.FailCreate = false;
            var retried = await Pool.Acquire(speaker.Account.Id, ct);

            // assert
            voiceId.Should().BeNull("a failed clone means the stock voice");
            retried.Should().BeNull("the clone isn't retried within the cool-down");
            Pool.InFlightCount.Should().Be(0, "the cool-down starts nothing");
            Soniox.CreateCount.Should().Be(createCount + 1, "only the failed attempt reached Soniox");
            var record = await UserVoices.Get(speaker.Account.Id, ct);
            record!.Status.Should().Be(UserVoiceStatus.Failed);
            record.SonioxVoiceId.Should().BeEmpty();
            record.FailedUntil.Should().NotBeNull();
            record.FailedUntil!.Value.Should().BeGreaterThan(Clocks.SystemClock.Now,
                "the cool-down is still running");
            (await Blobs.Exists(blobId, ct)).Should().BeFalse(
                "the failed attempt's sample is deleted, the retry after the cool-down rebuilds it");
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
        var voiceId = await Pool.AcquireSettled(speaker.Account.Id, ct);
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
        record.SampleHash.Should().Be(VoiceSampleBuilder.HashOf(speaker.Entry.Id),
            "the hash stays on the record, only the WAV goes with the clone");
        var blobId = VoiceSampleBuilder.BlobIdOf(speaker.Account.Id, record.SampleHash);
        (await Blobs.Exists(blobId, ct)).Should().BeFalse("the sample is deleted with the idle clone");
    }

    [Fact(Timeout = 120_000)]
    public async Task SweepShouldReconcileWithSoniox()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var voiceId = await Pool.AcquireSettled(speaker.Account.Id, ct);
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
        var voiceId = await Pool.AcquireSettled(speaker.Account.Id, ct);
        var record = await UserVoices.Get(speaker.Account.Id, ct);
        var createCount = Soniox.CreateCount;
        var othersEntry = await RecordSomeoneElsesEntry();
        await SetSettings(Tester, x => x with { OwnVoiceSampleEntryId = othersEntry.Id });

        // act
        var afterFailure = await Pool.AcquireSettled(speaker.Account.Id, ct);
        var again = await Pool.AcquireSettled(speaker.Account.Id, ct);

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
        var voiceId = await Pool.AcquireSettled(speaker.Account.Id, ct);
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
        var voiceId = await Pool.AcquireSettled(account.Id, ct);

        // assert
        voiceId.Should().BeNull("another user's recording is no sample of this one's voice");
        Soniox.CreateCount.Should().Be(createCount, "nothing reaches Soniox");
        (await Blobs.Exists(blobId, ct)).Should().BeFalse("no sample blob is written");
        var record = await UserVoices.Get(account.Id, ct);
        (record?.Status ?? UserVoiceStatus.None).Should().Be(UserVoiceStatus.None, "a missing sample isn't a failure");
    }

    [Fact(Timeout = 120_000)]
    public async Task SweepShouldReleaseEvenWhenSonioxListFails()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var voiceId = await Pool.AcquireSettled(speaker.Account.Id, ct);
        var record = await UserVoices.Get(speaker.Account.Id, ct);
        var idleSince = Clocks.SystemClock.Now - Constants.Audio.VoiceCloneIdleTimeout - TimeSpan.FromMinutes(1);
        var idleDiff = new UserVoiceDiff { LastUsedAt = idleSince };
        await Commander.Call(
            new UserVoicesBackend_Change(speaker.Account.Id, record!.Version, Change.Update(idleDiff)), ct);
        Soniox.FailList = true;
        try {
            // act
            await Sweeper.SweepOnce(ct, mustReconcile: true);
        }
        finally {
            Soniox.FailList = false;
        }

        // assert
        (await Soniox.Get(voiceId!, ct)).Should().BeNull("a failed reconcile doesn't hold up the idle release");
        record = await UserVoices.Get(speaker.Account.Id, ct);
        record!.Status.Should().Be(UserVoiceStatus.None);
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
        var voiceId = await Pool.AcquireSettled(speaker.Account.Id, ct);

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
    public async Task VanishedSampleShouldBeRebuiltNotFailed()
    {
        // arrange: a same-named leftover rejects the first create, and the sample is deleted under it -
        // as a Release on another host does between the builder finding the WAV and the clone reading it
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var hash = VoiceSampleBuilder.HashOf(speaker.Entry.Id);
        var blobId = VoiceSampleBuilder.BlobIdOf(speaker.Account.Id, hash);
        var orphan = await Soniox.Create(Pool.NameOf(speaker.Account.Id, hash), Stream.Null, ct);
        var createCount = Soniox.CreateCount;
        var deleteTask = (Task?)null;
        Soniox.OnCreate = _ => deleteTask ??= Blobs.Delete(blobId, ct);
        try {
            // act
            var voiceId = await Pool.AcquireSettled(speaker.Account.Id, ct);

            // assert
            voiceId.Should().NotBeNullOrEmpty("a sample that vanished mid-clone is rebuilt, not a failure");
            voiceId.Should().NotBe(orphan.Id);
            Soniox.CreateCount.Should().Be(createCount + 2, "the rejected attempt and the one after the rebuild");
            deleteTask.Should().NotBeNull("the sample was deleted under the first attempt");
            deleteTask!.IsCompletedSuccessfully.Should().BeTrue();
            (await Blobs.Exists(blobId, ct)).Should().BeTrue("the sample is rebuilt under the same hash");
            var record = await UserVoices.Get(speaker.Account.Id, ct);
            record!.Status.Should().Be(UserVoiceStatus.Ready);
            record.SonioxVoiceId.Should().Be(voiceId);
            record.SampleHash.Should().Be(hash);
            record.FailedUntil.Should().BeNull("no cool-down is set for a missing blob");
        }
        finally {
            Soniox.OnCreate = null;
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task OptOutShouldDropTheClone()
    {
        // arrange
        var speaker = await SignInWithSample(Tester);
        var ct = CancellationToken.None;
        var voiceId = await Pool.AcquireSettled(speaker.Account.Id, ct);
        await SetSettings(Tester, x => x with { IsOwnVoiceEnabled = false });
        var createCount = Soniox.CreateCount;

        // act
        var afterOptOut = await Pool.AcquireSettled(speaker.Account.Id, ct);

        // assert
        voiceId.Should().NotBeNullOrEmpty();
        afterOptOut.Should().BeNull("an opted-out speaker is dubbed with the stock voice");
        Soniox.CreateCount.Should().Be(createCount);
        (await Soniox.Get(voiceId!, ct)).Should().BeNull("opting out deletes the clone");
        var record = await UserVoices.Get(speaker.Account.Id, ct);
        record!.Status.Should().Be(UserVoiceStatus.None);
        record.SonioxVoiceId.Should().BeEmpty();
        var blobId = VoiceSampleBuilder.BlobIdOf(speaker.Account.Id, record.SampleHash);
        (await Blobs.Exists(blobId, ct)).Should().BeFalse("opting out deletes the sample with the clone");
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
