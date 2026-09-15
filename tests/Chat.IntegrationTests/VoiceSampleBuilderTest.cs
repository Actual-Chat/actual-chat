using ActualChat.Audio;
using ActualChat.Blobs;
using ActualChat.Streaming.Services;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public class VoiceSampleBuilderTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    // 20 ms frames, so 600 of them make a 12 s entry - comfortably above the 5 s minimum
    private const int EntryFrameCount = 600;

    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private VoiceSampleBuilder Builder => field ??= Tester.AppServices.GetRequiredService<VoiceSampleBuilder>();
    private IBlobStorage Blobs
        => field ??= Tester.AppServices.GetRequiredService<IBlobStorages>()[BlobScope.AudioRecord];

    [Fact(Timeout = 300_000)]
    public async Task EnoughRecordingsShouldProduceAWavSampleThatIsReusedForTheSameSelection()
    {
        // arrange
        var account = await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        await RegisterView(chatId);
        var entries = new List<ChatEntry>();
        for (var i = 0; i < 3; i++)
            entries.Add(await Tester.RecordVoiceEntry(chatId, Languages.English, frameCount: EntryFrameCount));
        var ct = CancellationToken.None;

        // act
        var (sample, failure) = await Builder.Build(account.Id, new UserLanguageSettings(), ct);

        // assert
        failure.Should().Be(VoiceSampleFailure.None);
        sample.Should().NotBeNull();
        var selected = VoiceSampleBuilder.SelectEntries(entries, Tester.AppServices.Clocks().SystemClock.Now);
        selected.Should().HaveCount(3, "three 12 s entries all fit under 60 s");
        sample!.Hash.Should().Be(VoiceSampleBuilder.HashOf(selected.Select(x => x.Id)),
            "the hash is over the ordered selection");
        sample.BlobId.Should().Be(VoiceSampleBuilder.BlobIdOf(account.Id, sample.Hash));
        sample.Duration.Should().BeGreaterThanOrEqualTo(Constants.Audio.VoiceSampleMinDuration)
            .And.BeLessThanOrEqualTo(Constants.Audio.VoiceSampleMaxDuration);
        var wav = await ReadBlob(sample.BlobId);
        var pcmLength = WavWriter.GetPcmLength(wav);
        pcmLength.Should().Be(wav.Length - WavWriter.HeaderLength, "the header must describe the PCM that follows");
        PcmDuration(pcmLength).Should().Be(sample.Duration);

        // act - the stored blob is swapped for a 1 s clip; a rebuild would put the real one back
        await Blobs.Delete(sample.BlobId, ct);
        await WriteSilence(sample.BlobId, TimeSpan.FromSeconds(1));
        var (again, againFailure) = await Builder.Build(account.Id, new UserLanguageSettings(), ct);

        // assert
        againFailure.Should().Be(VoiceSampleFailure.None);
        again!.Hash.Should().Be(sample.Hash, "the selection hasn't changed");
        again.BlobId.Should().Be(sample.BlobId);
        again.Duration.Should().Be(TimeSpan.FromSeconds(1), "an existing blob for the hash is reused, not rebuilt");
    }

    [Fact(Timeout = 120_000)]
    public async Task TooFewRecordingsShouldReportNotEnoughRecordings()
    {
        // arrange
        var account = await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        await RegisterView(chatId);
        await Tester.RecordVoiceEntry(chatId, Languages.English, frameCount: EntryFrameCount);

        // act
        var (sample, failure) = await Builder.Build(account.Id, new UserLanguageSettings(), CancellationToken.None);

        // assert
        sample.Should().BeNull();
        failure.Should().Be(VoiceSampleFailure.NotEnoughRecordings, "12 s is short of the 30 s minimum");
    }

    [Fact(Timeout = 120_000)]
    public async Task AnExplicitSampleShouldBeUsedInsteadOfTheRecordings()
    {
        // arrange
        var account = await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.English, frameCount: EntryFrameCount);
        var mediaId = entry.Audio!.MediaId!;
        var settings = new UserLanguageSettings { OwnVoiceSampleMediaId = mediaId };
        var ct = CancellationToken.None;

        // act
        var (sample, failure) = await Builder.Build(account.Id, settings, ct);
        var (missing, missingFailure) = await Builder.Build(account.Id,
            settings with { OwnVoiceSampleMediaId = MediaId.New(chatId.Value) }, ct);

        // assert
        failure.Should().Be(VoiceSampleFailure.None);
        sample!.Hash.Should().Be(VoiceSampleBuilder.HashOf(mediaId), "an explicit sample is keyed by its media id");
        sample.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1));
        (await Blobs.Exists(sample.BlobId, ct)).Should().BeTrue();
        missing.Should().BeNull();
        missingFailure.Should().Be(VoiceSampleFailure.SampleMissing, "the media doesn't exist");
    }

    // Private methods

    private Task RegisterView(ChatId chatId)
    {
        // What ChatView does when a group chat is opened - that's how it reaches the recency list
        var command = new ChatUsages_RegisterUsage {
            Session = Tester.Session,
            Kind = ChatUsageListKind.ViewedGroupChats,
            ChatId = chatId,
        };
        return Tester.AppServices.Commander().Call(command, CancellationToken.None);
    }

    private async Task<byte[]> ReadBlob(string blobId)
    {
        var stream = await Blobs.Read(blobId, CancellationToken.None);
        stream.Should().NotBeNull("the sample blob must exist");
        await using var _ = stream!;
        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private async Task WriteSilence(string blobId, TimeSpan duration)
    {
        var stream = new MemoryStream();
        WavWriter.Write(stream, new byte[PcmLength(duration)], Constants.Audio.RecordingSampleRate);
        stream.Position = 0;
        await Blobs.Write(blobId, stream, "audio/wav", CancellationToken.None);
    }

    private static int PcmLength(TimeSpan duration)
        => (int)(duration.TotalSeconds * Constants.Audio.RecordingSampleRate * sizeof(short));

    private static TimeSpan PcmDuration(int pcmLength)
        => TimeSpan.FromSeconds((double)pcmLength / (Constants.Audio.RecordingSampleRate * sizeof(short)));
}
