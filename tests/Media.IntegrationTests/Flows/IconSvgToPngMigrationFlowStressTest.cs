using ActualChat.App.Server.Flows;
using ActualChat.Testing.Host;
using ActualChat.Uploads;

namespace ActualChat.Media.IntegrationTests.Flows;

[Collection(nameof(MediaCollection))]
[Trait("Category", "Slow")]
public class IconSvgToPngMigrationFlowStressTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
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

    // countPerPhase must be > IconSvgToPngMigrationFlow.BatchSize (50) so that
    // each phase pages through at least one full batch + one partial batch,
    // exercising the LastProcessedEntityId cursor.
    [Theory]
    [InlineData(51)]
    [InlineData(201)]
    public async Task ShouldConvertManyEntitiesAcrossAllPhases(int countPerPhase)
    {
        // arrange — sign in as the test user that will own all seeded entities
        var account = await Tester.SignInAsUniqueBob();

        var avatarSvgIds = new MediaId[countPerPhase];
        var avatarIds = new Symbol[countPerPhase];
        var chatSvgIds = new MediaId[countPerPhase];
        var chatIds = new ChatId[countPerPhase];
        var placeSvgIds = new MediaId[countPerPhase];
        var placeIds = new PlaceId[countPerPhase];

        for (var i = 0; i < countPerPhase; i++) {
            avatarSvgIds[i] = await CreateSvgMedia($"avatar-{i}.svg");
            avatarIds[i] = await CreateAvatar(avatarSvgIds[i]);

            chatSvgIds[i] = await CreateSvgMedia($"chat-{i}.svg");
            (chatIds[i], _) = await Tester.CreateChat(diff => diff with {
                IsPublic = true,
                MediaId = chatSvgIds[i],
            });

            placeSvgIds[i] = await CreateSvgMedia($"place-{i}.svg");
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
            for (var i = 0; i < countPerPhase; i++) {
                await AssertAvatarRepointedToPng(avatarIds[i], avatarSvgIds[i], ct);
                await AssertChatRepointedToPng(chatIds[i], chatSvgIds[i], ct);
                await AssertPlaceRepointedToPng(placeIds[i], placeSvgIds[i], ct);
            }
        });
    }

    // Private methods

    private async Task<MediaId> CreateSvgMedia(string fileName)
    {
        var mediaId = MediaId.New("test-chat");
        var file = new UploadedStreamFile(
            fileName,
            "image/svg+xml",
            TestSvgBytes.Length,
            () => Task.FromResult<Stream>(new MemoryStream(TestSvgBytes)));
        // MediaSaver.Save persists the blob and creates the MediaFull row.
        // No image processing happens here — the SVG bytes survive intact.
        // Kind is Unknown (the default 0 in the DB) to mimic the legacy
        // production rows the migration is built for: old media records
        // were written before MediaKind existed and have Kind = Unknown.
        await MediaSaver.Save(mediaId, file, new Size2D(100, 100), MediaKind.Unknown, CancellationToken.None);
        return mediaId;
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
