using ActualChat.App.Server.Flows;
using ActualChat.Testing.Host;
using ActualChat.Uploads;

namespace ActualChat.Media.IntegrationTests.Flows;

[Collection(nameof(MediaCollection))]
public class IconSvgToPngMigrationFlowTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string ReplacesMediaIdKey = "ReplacesMediaId";

    private static readonly byte[] TestSvgBytes = """
        <?xml version="1.0" encoding="UTF-8"?>
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
          <circle cx="50" cy="50" r="40" fill="blue"/>
        </svg>
        """u8.ToArray();

    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IBlobStorage BlobStorage { get; } = fixture.AppHost.Services.BlobStorages()[BlobScope.ContentRecord];
    private IMediaSaver MediaSaver { get; } = fixture.AppHost.Services.GetRequiredService<IMediaSaver>();
    private IMediaBackend MediaBackend { get; } = fixture.AppHost.Services.GetRequiredService<IMediaBackend>();
    private IAvatars Avatars { get; } = fixture.AppHost.Services.GetRequiredService<IAvatars>();

    private AccountFull _account = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _account = await Tester.SignInAsUniqueBob();
    }

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldConvertSvgMediaToPng()
    {
        // arrange
        var (svgMediaId, svgBlobId) = await CreateMedia(
            TestSvgBytes, "image/svg+xml", MediaKind.UserAvatarPicture, "avatar.svg");
        var avatarId = await CreateAvatar(svgMediaId);

        // act
        await RunFlow();

        // assert: avatar points at a new PNG MediaId
        await AssertFlow(async ct => {
            var newMediaId = await GetAvatarMediaId(avatarId, ct);
            newMediaId.Should().NotBeNull();
            newMediaId.Value.Should().NotBe(svgMediaId.Value);

            var pngMedia = await MediaBackend.GetFull(newMediaId, ct);
            pngMedia.Should().NotBeNull();
            pngMedia.ContentType.Should().Be("image/png");
            pngMedia.BlobId.Should().EndWith(".png");
            pngMedia.Width.Should().Be(Constants.Attachments.MaxIconSize);
            pngMedia.Height.Should().Be(Constants.Attachments.MaxIconSize);
        });

        // assert: both PNG and original SVG blobs exist
        var newAvatarMediaId = await GetAvatarMediaId(avatarId, CancellationToken.None);
        var pngRow = await MediaBackend.GetFull(newAvatarMediaId, CancellationToken.None);
        await AssertBlobExists(pngRow!.BlobId);
        await AssertBlobExists(svgBlobId);
    }

    [Fact]
    public async Task ShouldStoreReplacesMediaIdOnNewPngRow()
    {
        // arrange
        var (svgMediaId, _) = await CreateMedia(
            TestSvgBytes, "image/svg+xml", MediaKind.UserAvatarPicture, "avatar.svg");
        var avatarId = await CreateAvatar(svgMediaId);

        // act
        await RunFlow();

        // assert: new PNG row carries ReplacesMediaId pointing back at the original SVG
        await AssertFlow(async ct => {
            var newMediaId = await GetAvatarMediaId(avatarId, ct);
            newMediaId.Should().NotBeNull();
            var pngMedia = await MediaBackend.GetFull(newMediaId, ct);
            pngMedia.Should().NotBeNull();
            var replacesMediaId = pngMedia.Metadata[ReplacesMediaIdKey];
            replacesMediaId.Should().Be(svgMediaId.Value);
        });
    }

    [Fact]
    public async Task ShouldLeaveOriginalSvgRowAndBlobIntact()
    {
        // arrange
        var (svgMediaId, svgBlobId) = await CreateMedia(
            TestSvgBytes, "image/svg+xml", MediaKind.UserAvatarPicture, "avatar.svg");
        await CreateAvatar(svgMediaId);
        var originalSvg = await MediaBackend.GetFull(svgMediaId, CancellationToken.None);
        originalSvg.Should().NotBeNull();
        var originalVersion = originalSvg.Version;

        // act
        await RunFlow();

        // assert: original SVG row and blob are byte-for-byte intact
        await AssertFlow(async ct => {
            var stillSvg = await MediaBackend.GetFull(svgMediaId, ct);
            stillSvg.Should().NotBeNull();
            stillSvg.BlobId.Should().Be(svgBlobId);
            stillSvg.ContentType.Should().Be("image/svg+xml");
            stillSvg.FileName.Should().Be("avatar.svg");
            stillSvg.Width.Should().Be(100);
            stillSvg.Height.Should().Be(100);
            stillSvg.Length.Should().Be(TestSvgBytes.Length);
            stillSvg.Kind.Should().Be(MediaKind.UserAvatarPicture);
            stillSvg.Version.Should().Be(originalVersion);
        });
        await AssertBlobExists(svgBlobId);
    }

    [Fact]
    public async Task ShouldBeIdempotentOnRerun()
    {
        // arrange
        var (svgMediaId, _) = await CreateMedia(
            TestSvgBytes, "image/svg+xml", MediaKind.UserAvatarPicture, "avatar.svg");
        var avatarId = await CreateAvatar(svgMediaId);

        // act 1
        await RunFlow();
        MediaId? mediaIdAfterFirstRun = null;
        await AssertFlow(async ct => {
            mediaIdAfterFirstRun = await GetAvatarMediaId(avatarId, ct);
            mediaIdAfterFirstRun.Should().NotBeNull();
            mediaIdAfterFirstRun!.Value.Should().NotBe(svgMediaId.Value);
        });

        // act 2: re-run from a clean flow state
        await RunFlow();

        // assert: second run converts nothing; avatar MediaId unchanged
        await AssertFlow(async ct => {
            var flow = await FlowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow.ConvertedCount.Should().Be(0);

            var mediaIdAfterSecondRun = await GetAvatarMediaId(avatarId, ct);
            mediaIdAfterSecondRun.Should().Be(mediaIdAfterFirstRun);
        });
    }

    [Fact]
    public async Task ShouldSkipNonSvgMedia()
    {
        // arrange
        var pngBytes = TestImages.CreatePng(50, 50);
        var (pngMediaId, pngBlobId) = await CreateMedia(
            pngBytes, "image/png", MediaKind.UserAvatarPicture, "avatar.png", width: 50, height: 50);
        var avatarId = await CreateAvatar(pngMediaId);

        // act
        await RunFlow();

        // assert: avatar still points at the original PNG
        await AssertFlow(async ct => {
            var mediaIdAfter = await GetAvatarMediaId(avatarId, ct);
            mediaIdAfter.Should().Be(pngMediaId);

            var unchanged = await MediaBackend.GetFull(pngMediaId, ct);
            unchanged.Should().NotBeNull();
            unchanged.ContentType.Should().Be("image/png");
            unchanged.BlobId.Should().Be(pngBlobId);
        });
    }

    [Fact]
    public async Task ShouldHandleMissingBlob()
    {
        // arrange: chat SVG with its blob removed
        var (svgMediaId, svgBlobId) = await CreateMedia(
            TestSvgBytes, "image/svg+xml", MediaKind.ChatPicture, "missing.svg", deleteBlob: true);
        var chatId = await CreateChat(svgMediaId);

        // act
        await RunFlow();

        // assert: missing blob -> skipped; chat still points at the original SVG
        await AssertFlow(async ct => {
            var mediaIdAfter = await GetChatMediaId(chatId, ct);
            mediaIdAfter.Should().Be(svgMediaId);

            var stillSvg = await MediaBackend.GetFull(svgMediaId, ct);
            stillSvg.Should().NotBeNull();
            stillSvg.ContentType.Should().Be("image/svg+xml");
            stillSvg.BlobId.Should().Be(svgBlobId);
        });
    }

    [Fact]
    public async Task ShouldSkipSystemIcons()
    {
        // arrange: SVG in "system-icons" scope referenced by an avatar.
        // MediaDbInitializer upgrades these in place on startup, so the flow must skip them.
        var systemIconMediaId = MediaId.New("system-icons");
        var (svgMediaId, svgBlobId) = await CreateMediaWithId(
            systemIconMediaId, TestSvgBytes, "image/svg+xml", MediaKind.UserAvatarPicture, "system-icon.svg");
        var avatarId = await CreateAvatar(svgMediaId);

        // act
        await RunFlow();

        // assert: avatar, media row, and blob all untouched
        await AssertFlow(async ct => {
            var mediaIdAfter = await GetAvatarMediaId(avatarId, ct);
            mediaIdAfter.Should().Be(svgMediaId);

            var media = await MediaBackend.GetFull(svgMediaId, ct);
            media.Should().NotBeNull();
            media.ContentType.Should().Be("image/svg+xml");
            media.BlobId.Should().Be(svgBlobId);
            media.FileName.Should().Be("system-icon.svg");
        });
        await AssertBlobExists(svgBlobId);
    }

    [Fact]
    public async Task ShouldConvertPlaceBackgroundMediaId()
    {
        // arrange: place with only BackgroundMediaId set — exercises the
        // IsBackground=true branch in GetPlaceBatch flatten + RepointPlace.
        var (bgSvgId, _) = await CreateMedia(
            TestSvgBytes, "image/svg+xml", MediaKind.ChatPicture, "place-bg.svg");
        var placeId = await CreatePlaceWithBackground(bgSvgId);

        // act
        await RunFlow();

        // assert: BackgroundMediaId repointed to a new PNG; original SVG row intact
        await AssertFlow(async ct => {
            var place = await Tester.Places.Get(Tester.Session, placeId, ct);
            place.Should().NotBeNull();
            place.BackgroundMediaId.Should().NotBeNull();
            place.BackgroundMediaId!.Value.Should().NotBe(bgSvgId.Value);
            await AssertRepointedToPng(bgSvgId, place.BackgroundMediaId, ct);

            var stillSvg = await MediaBackend.GetFull(bgSvgId, ct);
            stillSvg.Should().NotBeNull();
            stillSvg.ContentType.Should().Be("image/svg+xml");
        });
    }

    [Fact]
    public async Task ShouldConvertPlaceMediaIdAndBackgroundMediaIdIndependently()
    {
        // arrange: place with distinct SVGs in both slots
        var (fgSvgId, _) = await CreateMedia(
            TestSvgBytes, "image/svg+xml", MediaKind.ChatPicture, "place-fg.svg");
        var (bgSvgId, _) = await CreateMedia(
            TestSvgBytes, "image/svg+xml", MediaKind.ChatPicture, "place-bg.svg");
        var place = await Tester.CreatePlace(diff => diff with {
            IsPublic = true,
            MediaId = fgSvgId,
            BackgroundMediaId = bgSvgId,
        });

        // act
        await RunFlow();

        // assert: both slots repointed to distinct PNG MediaIds
        await AssertFlow(async ct => {
            var updated = await Tester.Places.Get(Tester.Session, place.Id, ct);
            updated.Should().NotBeNull();
            updated.MediaId.Should().NotBeNull();
            updated.BackgroundMediaId.Should().NotBeNull();
            updated.MediaId!.Value.Should().NotBe(fgSvgId.Value);
            updated.BackgroundMediaId!.Value.Should().NotBe(bgSvgId.Value);
            updated.MediaId!.Value.Should().NotBe(updated.BackgroundMediaId!.Value);
            await AssertRepointedToPng(fgSvgId, updated.MediaId, ct);
            await AssertRepointedToPng(bgSvgId, updated.BackgroundMediaId, ct);
        });
    }

    [Fact]
    public async Task ShouldConvertMediaReferencedByAvatarChatAndPlace()
    {
        // arrange: one SVG per entity kind
        var (avatarSvgId, _) = await CreateMedia(
            TestSvgBytes, "image/svg+xml", MediaKind.UserAvatarPicture, "avatar.svg");
        var (chatSvgId, _) = await CreateMedia(
            TestSvgBytes, "image/svg+xml", MediaKind.ChatPicture, "chat.svg");
        var (placeSvgId, _) = await CreateMedia(
            TestSvgBytes, "image/svg+xml", MediaKind.ChatPicture, "place.svg");

        var avatarId = await CreateAvatar(avatarSvgId);
        var chatId = await CreateChat(chatSvgId);
        var placeId = await CreatePlace(placeSvgId);

        // act
        await RunFlow();

        // assert: each entity repointed to a new PNG
        await AssertFlow(async ct => {
            await AssertRepointedToPng(avatarSvgId, await GetAvatarMediaId(avatarId, ct), ct);
            await AssertRepointedToPng(chatSvgId, await GetChatMediaId(chatId, ct), ct);
            await AssertRepointedToPng(placeSvgId, await GetPlaceMediaId(placeId, ct), ct);
        });
    }

    // Private methods

    private async Task AssertRepointedToPng(MediaId originalSvgId, MediaId? newMediaId, CancellationToken ct)
    {
        newMediaId.Should().NotBeNull();
        newMediaId.Value.Should().NotBe(originalSvgId.Value);
        var pngMedia = await MediaBackend.GetFull(newMediaId, ct);
        pngMedia.Should().NotBeNull();
        pngMedia.ContentType.Should().Be("image/png");
        pngMedia.BlobId.Should().EndWith(".png");
    }

    private Task<(MediaId MediaId, string BlobId)> CreateMedia(
        byte[] blobBytes,
        string contentType,
        MediaKind kind,
        string fileName,
        int width = 100,
        int height = 100,
        bool deleteBlob = false)
        => CreateMediaWithId(MediaId.New("test-chat"), blobBytes, contentType, kind, fileName, width, height, deleteBlob);

    private async Task<(MediaId MediaId, string BlobId)> CreateMediaWithId(
        MediaId mediaId,
        byte[] blobBytes,
        string contentType,
        MediaKind kind,
        string fileName,
        int width = 100,
        int height = 100,
        bool deleteBlob = false)
    {
        var file = new UploadedStreamFile(
            fileName,
            contentType,
            blobBytes.Length,
            () => Task.FromResult<Stream>(new MemoryStream(blobBytes)));
        var mediaRef = await MediaSaver.Save(mediaId, file, new Size2D(width, height), kind, CancellationToken.None);
        if (deleteBlob)
            await BlobStorage.Delete(mediaRef.BlobId, CancellationToken.None);
        return (mediaId, mediaRef.BlobId);
    }

    // Reschedules the flow from a clean state.
    private Task RunFlow()
        => FlowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

    // Waits until the flow completes and `assertion` passes, or times out.
    private Task AssertFlow(Func<CancellationToken, Task> assertion)
        => ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow.UntypedResult.Should().NotBeNull();
            await assertion(ct).ConfigureAwait(false);
        }, TimeSpan.FromSeconds(30));

    private async Task AssertBlobExists(string blobId)
    {
        var stream = await BlobStorage.Read(blobId, CancellationToken.None);
        stream.Should().NotBeNull();
        await using var _ = stream.ConfigureAwait(false);
        stream.Length.Should().BeGreaterThan(0);
    }

    private async Task<Symbol> CreateAvatar(MediaId mediaId)
    {
        var command = new Avatars_Change {
            Session = Tester.Session,
            AvatarId = Symbol.Empty,
            ExpectedVersion = null,
            Change = Change.Create(new AvatarDiff {
                Name = "test",
                MediaId = Option.Some<MediaId?>(mediaId),
            }),
        };
        var avatar = await Commander.Call(command, true, CancellationToken.None);
        return avatar.Id;
    }

    private async Task<ChatId> CreateChat(MediaId mediaId)
    {
        var (chatId, _) = await Tester.CreateChat(diff => diff with {
            IsPublic = true,
            MediaId = mediaId,
        });
        return chatId;
    }

    private async Task<PlaceId> CreatePlace(MediaId mediaId)
    {
        var place = await Tester.CreatePlace(diff => diff with {
            IsPublic = true,
            MediaId = mediaId,
        });
        return place.Id;
    }

    private async Task<PlaceId> CreatePlaceWithBackground(MediaId backgroundMediaId)
    {
        var place = await Tester.CreatePlace(diff => diff with {
            IsPublic = true,
            BackgroundMediaId = backgroundMediaId,
        });
        return place.Id;
    }

    private async Task<MediaId?> GetAvatarMediaId(Symbol avatarId, CancellationToken cancellationToken)
    {
        var avatar = await Avatars.GetOwn(Tester.Session, avatarId, cancellationToken);
        return avatar?.MediaId;
    }

    private async Task<MediaId?> GetChatMediaId(ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Tester.Chats.Get(Tester.Session, chatId, cancellationToken);
        return chat?.MediaId;
    }

    private async Task<MediaId?> GetPlaceMediaId(PlaceId placeId, CancellationToken cancellationToken)
    {
        var place = await Tester.Places.Get(Tester.Session, placeId, cancellationToken);
        return place?.MediaId;
    }
}
