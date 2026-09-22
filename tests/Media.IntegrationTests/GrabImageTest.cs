using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Media.IntegrationTests;

[Collection(nameof(MediaCollection))]
public class GrabImageTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly RandomStringGenerator RandomStringGenerator = new (5, Alphabet.AlphaNumericLower);

    private HttpHandlerMock Http { get; } = fixture.AppHost.Services.GetRequiredService<HttpHandlerMock>();
    private IMediaBackend MediaBackend => field ??= AppHost.Services.GetRequiredService<IMediaBackend>();

    [Fact]
    public async Task GrabImageShouldStoreMediaForAnHttpPng()
    {
        // arrange
        var imageUrl = $"https://domain_{RandomStringGenerator.Next(3)}.some/{RandomStringGenerator.Next()}.png";
        Http.SetupImage(imageUrl, contentType: "image/png");

        // act
        var mediaId = await Commander.Call(new MediaBackend_GrabImage(imageUrl));

        // assert
        mediaId.Should().NotBeNull();
        var media = await MediaBackend.Get(mediaId, default);
        media!.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task GrabImageShouldReturnNullWhenFetchFails()
    {
        // act
        var mediaId = await Commander.Call(new MediaBackend_GrabImage("http://10.0.0.1/secret.png"));

        // assert
        mediaId.Should().BeNull();
    }
}
