using ActualChat.Testing.Host;

namespace ActualChat.Media.IntegrationTests;

[Collection(nameof(MediaCollection))]
public sealed class MediaUploadOwnershipTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task CannotProcessAnotherUsersUpload()
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        await using var otherUser = AppHost.NewBlazorTester(Out);
        await otherUser.SignInAsUniqueAlice();
        var data = "owner upload"u8.ToArray();
        var metadata = new MetadataBag()
            .Set("FileName", "test.txt")
            .Set("ContentType", "text/plain");
        var uploadId = await owner.Commander.Call(new Uploads_Create {
            Session = owner.Session,
            Length = data.Length,
            Tag = "",
            Metadata = metadata,
        });
        await owner.Commander.Call(new Uploads_Append {
            Session = owner.Session,
            UploadId = uploadId,
            Offset = 0,
            Chunk = data,
        });
        var ownerMediaId = await owner.Commander.Call(new Media_ReserveMedia {
            Session = owner.Session,
            Scope = "owner-media",
        });
        var otherMediaId = await otherUser.Commander.Call(new Media_ReserveMedia {
            Session = otherUser.Session,
            Scope = "other-media",
        });

        // act
        var otherError = await Record.ExceptionAsync(
            () => otherUser.Commander.Call(new Media_ProcessUpload {
                Session = otherUser.Session,
                MediaId = otherMediaId,
                UploadId = uploadId,
            }));
        MediaRef? ownerMedia = null;
        var ownerError = await Record.ExceptionAsync(async () => {
            ownerMedia = await owner.Commander.Call(
                new Media_ProcessUpload { Session = owner.Session, MediaId = ownerMediaId, UploadId = uploadId });
        });

        // assert
        otherError.Should().BeOfType<UploadNotFoundException>();
        ownerError.Should().BeNull();
        ownerMedia.Should().NotBeNull();
    }
}
