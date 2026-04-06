using ActualChat.Media.Resources;
using ActualChat.Testing.Host;

namespace ActualChat.Media.IntegrationTests;

[Collection(nameof(MediaCollection))]
public class SystemIconTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly Resource[] SystemIconResources = typeof(Resource)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(Resource))
        .Select(f => (Resource)f.GetValue(null)!)
        .ToArray();

    private IMediaBackend MediaBackend { get; } = fixture.AppHost.Services.GetRequiredService<IMediaBackend>();

    [Fact]
    public async Task EverySystemShouldBePng()
    {
        foreach (var resource in SystemIconResources) {
            var idValue = $"system-icons:{Path.GetFileNameWithoutExtension(resource.Name)}";
            var mediaId = MediaId.Parse(idValue);

            // act
            var media = await MediaBackend.GetFull(mediaId, CancellationToken.None);

            // assert
            media.Should().NotBeNull($"system icon '{idValue}' must be seeded by MediaDbInitializer");
            media.ContentType.Should().Be("image/png", $"system icon '{idValue}' must be seeded as PNG");
            media.BlobId.Should().EndWith(".png", $"system icon '{idValue}' blob must end with .png");
        }
    }
}
