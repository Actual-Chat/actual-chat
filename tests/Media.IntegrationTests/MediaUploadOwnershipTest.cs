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
        var metadata = new PropertyBag()
            .Set("FileName", "test.txt")
            .Set("ContentType", "text/plain");
        var uploadId = await owner.Commander.Call(new Uploads_Create(owner.Session, data.Length, "", metadata));
        await owner.Commander.Call(new Uploads_Append(owner.Session, uploadId, 0, data));
        var ownerMediaId = await owner.Commander.Call(new Media_ReserveMedia(owner.Session, "owner-media"));
        var otherMediaId = await otherUser.Commander.Call(new Media_ReserveMedia(otherUser.Session, "other-media"));

        // act
        var otherError = await Record.ExceptionAsync(
            () => otherUser.Commander.Call(new Media_ProcessUpload(otherUser.Session, otherMediaId, uploadId)));
        MediaRef? ownerMedia = null;
        var ownerError = await Record.ExceptionAsync(async () => {
            ownerMedia = await owner.Commander.Call(
                new Media_ProcessUpload(owner.Session, ownerMediaId, uploadId));
        });

        // assert
        otherError.Should().BeOfType<UploadNotFoundException>();
        ownerError.Should().BeNull();
        ownerMedia.Should().NotBeNull();
    }
}
