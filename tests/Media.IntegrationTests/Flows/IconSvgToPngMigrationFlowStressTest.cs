using ActualChat.App.Server.Flows;
using ActualChat.Testing.Host;
using ActualChat.Uploads;

namespace ActualChat.Media.IntegrationTests.Flows;

[Collection(nameof(MediaCollection))]
[Trait("Category", "Slow")]
public class IconSvgToPngMigrationFlowStressTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    // Must be > IconSvgToPngMigrationFlow.BatchSize (50) so each phase pages
    // through one full batch + one partial batch, exercising the cursor.
    private const int CountPerPhase = 60;

    private static readonly byte[] TestSvgBytes = """
        <?xml version="1.0" encoding="UTF-8"?>
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
          <circle cx="50" cy="50" r="40" fill="blue"/>
        </svg>
        """u8.ToArray();

    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IMediaSaver MediaSaver { get; } = fixture.AppHost.Services.GetRequiredService<IMediaSaver>();
    private IMediaBackend MediaBackend { get; } = fixture.AppHost.Services.GetRequiredService<IMediaBackend>();
    private IAvatars Avatars { get; } = fixture.AppHost.Services.GetRequiredService<IAvatars>();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldConvertManyEntitiesAcrossAllPhases()
    {
        // arrange — sign in as the test user that will own all seeded entities
        var account = await Tester.SignInAsUniqueBob();

        var avatarSvgIds = new MediaId[CountPerPhase];
        var avatarIds = new Symbol[CountPerPhase];
        var chatSvgIds = new MediaId[CountPerPhase];
        var chatIds = new ChatId[CountPerPhase];
        var placeSvgIds = new MediaId[CountPerPhase];
        var placeIds = new PlaceId[CountPerPhase];

        for (var i = 0; i < CountPerPhase; i++) {
            avatarSvgIds[i] = await CreateSvgMedia(MediaKind.UserAvatarPicture, $"avatar-{i}.svg");
            avatarIds[i] = await CreateAvatar(account.Id, avatarSvgIds[i]);

            chatSvgIds[i] = await CreateSvgMedia(MediaKind.ChatPicture, $"chat-{i}.svg");
            (chatIds[i], _) = await Tester.CreateChat(diff => diff with {
                IsPublic = true,
                MediaId = chatSvgIds[i],
            });

            placeSvgIds[i] = await CreateSvgMedia(MediaKind.ChatPicture, $"place-{i}.svg");
            var place = await Tester.CreatePlace(diff => diff with {
                IsPublic = true,
                MediaId = placeSvgIds[i],
            });
            placeIds[i] = place.Id;
        }

        // act
        await RunFlow();

        // assert: every seeded host entity has been repointed to a new MediaId pointing at a PNG
        await AssertFlow(async ct => {
            for (var i = 0; i < CountPerPhase; i++) {
                await AssertAvatarRepointedToPng(avatarIds[i], avatarSvgIds[i], ct);
                await AssertChatRepointedToPng(chatIds[i], chatSvgIds[i], ct);
                await AssertPlaceRepointedToPng(placeIds[i], placeSvgIds[i], ct);
            }
        });
    }

    // Private methods

    private async Task<MediaId> CreateSvgMedia(MediaKind kind, string fileName)
    {
        var mediaId = MediaId.New("test-chat");
        var file = new UploadedStreamFile(
            fileName,
            "image/svg+xml",
            TestSvgBytes.Length,
            () => Task.FromResult<Stream>(new MemoryStream(TestSvgBytes)));
        // MediaSaver.Save persists the blob and creates the MediaFull row.
        // No image processing happens here — the SVG bytes survive intact,
        // which is the same shape production has for the legacy SVG icons
        // we're trying to migrate.
        await MediaSaver.Save(mediaId, file, new Size(100, 100), kind, CancellationToken.None);
        return mediaId;
    }

    private async Task<Symbol> CreateAvatar(UserId userId, MediaId mediaId)
    {
        var command = new Avatars_Change(
            Tester.Session,
            Symbol.Empty,
            null,
            new Change<AvatarFull> {
                Create = new AvatarFull(userId) {
                    Name = "test",
                    MediaId = mediaId,
                },
            });
        var avatar = await Commander.Call(command, true, CancellationToken.None);
        return avatar.Id;
    }

    private async Task AssertAvatarRepointedToPng(Symbol avatarId, MediaId originalSvgId, CancellationToken ct)
    {
        var avatar = await Avatars.GetOwn(Tester.Session, avatarId, ct);
        avatar.Should().NotBeNull();
        avatar.MediaId.Should().NotBeNull();
        avatar.MediaId!.Value.Should().NotBe(originalSvgId.Value);
        await AssertMediaIsPng(avatar.MediaId, ct);
    }

    private async Task AssertChatRepointedToPng(ChatId chatId, MediaId originalSvgId, CancellationToken ct)
    {
        var chat = await Tester.Chats.Get(Tester.Session, chatId, ct);
        chat.Should().NotBeNull();
        chat.MediaId.Should().NotBeNull();
        chat.MediaId!.Value.Should().NotBe(originalSvgId.Value);
        await AssertMediaIsPng(chat.MediaId, ct);
    }

    private async Task AssertPlaceRepointedToPng(PlaceId placeId, MediaId originalSvgId, CancellationToken ct)
    {
        var place = await Tester.Places.Get(Tester.Session, placeId, ct);
        place.Should().NotBeNull();
        place.MediaId.Should().NotBeNull();
        place.MediaId!.Value.Should().NotBe(originalSvgId.Value);
        await AssertMediaIsPng(place.MediaId, ct);
    }

    private async Task AssertMediaIsPng(MediaId mediaId, CancellationToken ct)
    {
        var media = await MediaBackend.GetFull(mediaId, ct);
        media.Should().NotBeNull();
        media.ContentType.Should().Be("image/png");
        media.BlobId.Should().EndWith(".png");
    }

    // Schedules the migration flow with reset.
    private Task RunFlow()
        => FlowHub.NewResumeEvent<IconSvgToPngMigrationFlow>().WithReset().Schedule();

    // Waits until the flow has completed and `assertion` passes (or times out).
    // Generous timeout — the flow processes hundreds of entities, each via a
    // full backend command pipeline (and the chats phase additionally walks
    // the place root chats that PlacesBackend cascades MediaId into).
    private Task AssertFlow(Func<CancellationToken, Task> assertion)
        => ComputedTest.When(async ct => {
            var flow = await FlowHub.TryGet<IconSvgToPngMigrationFlow>("", ct);
            flow.Should().NotBeNull();
            flow.UntypedResult.Should().NotBeNull();
            await assertion(ct).ConfigureAwait(false);
        }, TimeSpan.FromSeconds(180));
}
