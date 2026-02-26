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
    public async Task BeamAvatar_Svg_ReturnsValidSvg()
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
    public async Task BeamAvatar_Png_ReturnsValidPng()
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
    public async Task BeamAvatar_SameKey_ReturnsSameContent()
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
    public async Task BeamAvatar_DifferentKeys_ReturnsDifferentContent()
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
    public async Task BeamAvatar_WithSquareParameter_ReturnsSquareAvatar()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();
        var key = "squaretest";

        // Act
        var responseRound = await client.GetAsync($"/api/avatars/beam/{key}?square=false");
        var responseSquare = await client.GetAsync($"/api/avatars/beam/{key}?square=true");

        // Assert
        responseRound.IsSuccessStatusCode.Should().BeTrue();
        responseSquare.IsSuccessStatusCode.Should().BeTrue();

        var svgRound = await responseRound.Content.ReadAsStringAsync();
        var svgSquare = await responseSquare.Content.ReadAsStringAsync();

        svgRound.Should().Contain("rx='72'", "round avatar should have border radius");
        svgSquare.Should().NotContain("rx='72'", "square avatar should not have border radius");
    }

    [Fact]
    public async Task BeamAvatar_HasCacheHeaders()
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
    public async Task MarbleAvatar_Svg_ReturnsValidSvg()
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
    public async Task MarbleAvatar_Png_ReturnsValidPng()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();
        var key = "marbleuser456";

        // Act
        var response = await client.GetAsync($"/api/avatars/marble/{key}?format=png&size=120");

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
    public async Task MarbleAvatar_WithTitle_IncludesTitleInSvg()
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
    public async Task MarbleAvatar_WithDoNotBlur_RemovesBlurEffect()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();
        var key = "blurtest";

        // Act
        var responseWithBlur = await client.GetAsync($"/api/avatars/marble/{key}?doNotBlur=false");
        var responseWithoutBlur = await client.GetAsync($"/api/avatars/marble/{key}?doNotBlur=true");

        // Assert
        responseWithBlur.IsSuccessStatusCode.Should().BeTrue();
        responseWithoutBlur.IsSuccessStatusCode.Should().BeTrue();

        var svgWithBlur = await responseWithBlur.Content.ReadAsStringAsync();
        var svgWithoutBlur = await responseWithoutBlur.Content.ReadAsStringAsync();

        svgWithBlur.Should().Contain("feGaussianBlur", "should have blur effect by default");
        svgWithoutBlur.Should().NotContain("feGaussianBlur", "should not have blur when doNotBlur is true");
    }

    [Fact]
    public async Task MarbleAvatar_SameKey_ReturnsSameContent()
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
    public async Task MarbleAvatar_HasCacheHeaders()
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
    public async Task Avatar_EmptyKey_ReturnsBadRequest()
    {
        // Arrange
        using var client = AppHost.NewHttpClient();

        // Act
        var beamResponse = await client.GetAsync("/api/avatars/beam/");
        var marbleResponse = await client.GetAsync("/api/avatars/marble/");

        // Assert - should get 404 or similar error for empty key
        beamResponse.IsSuccessStatusCode.Should().BeFalse();
        marbleResponse.IsSuccessStatusCode.Should().BeFalse();
    }
}
