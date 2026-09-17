using ActualChat.Testing.Host;

namespace ActualChat.Media.IntegrationTests;

[Collection(nameof(ImageSuggestionsCollection))]
public sealed class MediaGenerateTest(ImageSuggestionsAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ImageSuggestionsAppHostFixture>(fixture, @out)
{
    private IMediaBackend MediaBackend => field ??= AppHost.Services.GetRequiredService<IMediaBackend>();

    [Fact]
    public async Task GeneratedMediaShouldBelongToItsCaller()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueAlice();

        // act
        var mediaRef = await tester.Commander.Call(new Media_Generate {
            Session = tester.Session,
            Scope = "generate-ownership",
            Description = "a stack of books",
        });

        // assert - the caller has to be able to remove it later, and that check is owner-only
        mediaRef.Should().NotBeNull();
        var media = await MediaBackend.GetFull(mediaRef!.MediaId, default);
        media.Should().NotBeNull();
        media!.UserId.Should().Be(account.Id);
    }

    [Fact]
    public async Task CallerShouldRemoveWhatItGenerated()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueAlice();
        var mediaRef = await tester.Commander.Call(new Media_Generate {
            Session = tester.Session,
            Scope = "generate-removal",
            Description = "a stack of books",
        });
        mediaRef.Should().NotBeNull();

        // act
        await tester.Commander.Call(new Media_RemoveMedia { Session = tester.Session, MediaId = mediaRef!.MediaId });

        // assert
        var media = await MediaBackend.GetFull(mediaRef.MediaId, default);
        media.Should().BeNull();
    }

    [Fact]
    public async Task OtherUserShouldNotRemoveIt()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueAlice();
        await using var otherUser = AppHost.NewBlazorTester(Out);
        await otherUser.SignInAsUniqueBob();
        var mediaRef = await owner.Commander.Call(new Media_Generate {
            Session = owner.Session,
            Scope = "generate-foreign-removal",
            Description = "a stack of books",
        });
        mediaRef.Should().NotBeNull();

        // act
        var error = await Record.ExceptionAsync(
            () => otherUser.Commander.Call(
                new Media_RemoveMedia { Session = otherUser.Session, MediaId = mediaRef!.MediaId }));

        // assert
        error.Should().BeOfType<UnauthorizedAccessException>();
        var media = await MediaBackend.GetFull(mediaRef!.MediaId, default);
        media.Should().NotBeNull();
    }
}
