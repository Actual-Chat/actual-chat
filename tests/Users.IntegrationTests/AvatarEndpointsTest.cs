using System.Net;
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class AvatarEndpointsTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldReturnValidBeamSvg()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();
        var key = "testuser123";

        // Act
        var response = await client.GetAsync($"/api/avatars/beam/{key}");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/svg+xml");

        var svg = await response.Content.ReadAsStringAsync();
        svg.Should().NotBeNullOrEmpty();
        svg.Should().StartWith("<svg");
        svg.Should().EndWith("</svg>");
        svg.Should().Contain("xmlns='http://www.w3.org/2000/svg'");
        svg.Should().Contain("viewBox='0 0 36 36'");
    }

    [Fact]
    public async Task ShouldReturnValidBeamPng()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();
        var key = "testuser456";

        // Act
        var response = await client.GetAsync($"/api/avatars/beam/{key}?format=png&size=80");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");

        var pngBytes = await response.Content.ReadAsByteArrayAsync();
        pngBytes.Should().NotBeEmpty();
        // PNG files start with specific magic bytes
        pngBytes[0].Should().Be(0x89);
        pngBytes[1].Should().Be(0x50); // 'P'
        pngBytes[2].Should().Be(0x4E); // 'N'
        pngBytes[3].Should().Be(0x47); // 'G'
    }

    [Fact]
    public async Task ShouldReturnSameBeamAvatarForSameKey()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();
        var key = "consistencytest";

        // Act
        var response1 = await client.GetAsync($"/api/avatars/beam/{key}");
        var response2 = await client.GetAsync($"/api/avatars/beam/{key}");

        // Assert
        response1.IsSuccessStatusCode.Should().BeTrue();
        response2.IsSuccessStatusCode.Should().BeTrue();

        var svg1 = await response1.Content.ReadAsStringAsync();
        var svg2 = await response2.Content.ReadAsStringAsync();
        svg1.Should().Be(svg2, "same key should produce identical SVG");
    }

    [Fact]
    public async Task ShouldReturnDifferentBeamAvatarsForDifferentKeys()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();

        // Act
        var response1 = await client.GetAsync("/api/avatars/beam/user1");
        var response2 = await client.GetAsync("/api/avatars/beam/user2");

        // Assert
        response1.IsSuccessStatusCode.Should().BeTrue();
        response2.IsSuccessStatusCode.Should().BeTrue();

        var svg1 = await response1.Content.ReadAsStringAsync();
        var svg2 = await response2.Content.ReadAsStringAsync();
        svg1.Should().NotBe(svg2, "different keys should produce different avatars");
    }

    [Fact]
    public async Task ShouldReturnCacheHeadersForBeamAvatar()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();
        var key = "cachetest";

        // Act
        var response = await client.GetAsync($"/api/avatars/beam/{key}");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().NotBeNull();
        response.Headers.CacheControl.MaxAge!.Value.TotalDays.Should().BeGreaterThanOrEqualTo(29);
    }

    [Fact]
    public async Task ShouldReturnValidMarbleSvg()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();
        var key = "marbleuser123";

        // Act
        var response = await client.GetAsync($"/api/avatars/marble/{key}");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/svg+xml");

        var svg = await response.Content.ReadAsStringAsync();
        svg.Should().NotBeNullOrEmpty();
        svg.Should().StartWith("<svg");
        svg.Should().EndWith("</svg>");
        svg.Should().Contain("xmlns='http://www.w3.org/2000/svg'");
        svg.Should().Contain("viewBox='0 0 80 80'");
    }

    [Fact]
    public async Task ShouldReturnValidMarblePng()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();
        var key = "marbleuser456";

        // Act
        var response = await client.GetAsync($"/api/avatars/marble/{key}?format=png&size=80");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");

        var pngBytes = await response.Content.ReadAsByteArrayAsync();
        pngBytes.Should().NotBeEmpty();
        // PNG magic bytes
        pngBytes[0].Should().Be(0x89);
        pngBytes[1].Should().Be(0x50);
    }

    [Fact]
    public async Task ShouldIncludeTitleInMarbleSvg()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();
        var key = "titletest";

        // Act
        var response = await client.GetAsync($"/api/avatars/marble/{key}?title=Alice");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();

        var svg = await response.Content.ReadAsStringAsync();
        svg.Should().Contain("<text");
        svg.Should().Contain(">A</text>", "should display uppercase first letter of title");
    }

    [Fact]
    public async Task ShouldReturnSameMarbleAvatarForSameKey()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();
        var key = "marbleconsistency";

        // Act
        var response1 = await client.GetAsync($"/api/avatars/marble/{key}");
        var response2 = await client.GetAsync($"/api/avatars/marble/{key}");

        // Assert
        response1.IsSuccessStatusCode.Should().BeTrue();
        response2.IsSuccessStatusCode.Should().BeTrue();

        var svg1 = await response1.Content.ReadAsStringAsync();
        var svg2 = await response2.Content.ReadAsStringAsync();
        svg1.Should().Be(svg2, "same key should produce identical SVG");
    }

    [Fact]
    public async Task ShouldReturnCacheHeadersForMarbleAvatar()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();
        var key = "marblecache";

        // Act
        var response = await client.GetAsync($"/api/avatars/marble/{key}");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().NotBeNull();
        response.Headers.CacheControl.MaxAge!.Value.TotalDays.Should().BeGreaterThanOrEqualTo(29);
    }

    [Fact]
    public async Task ShouldReturnNotFoundForEmptyKey()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();

        // Act
        var beamResponse = await client.GetAsync("/api/avatars/beam/");
        var marbleResponse = await client.GetAsync("/api/avatars/marble/");

        // Assert - empty key doesn't match the route, so 404 is returned
        beamResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        marbleResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
