using ActualChat.Media.Resources;
using ActualChat.Testing.Host;

namespace ActualChat.Media.IntegrationTests;

public class SystemIconTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(SystemIconTest)}", TestAppHostOptions.Default, @out)
{
    private static readonly Resource[] SystemIconResources = typeof(Resource)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(Resource))
        .Select(f => (Resource)f.GetValue(null)!)
        .ToArray();

    [Fact]
    public async Task EverySystemShouldBePng()
    {
        // arrange
        await using var h = await NewAppHost();
        var mediaBackend = h.Services.GetRequiredService<IMediaBackend>();

        foreach (var resource in SystemIconResources) {
            var idValue = $"system-icons:{Path.GetFileNameWithoutExtension(resource.Name)}";
            var mediaId = MediaId.Parse(idValue);

            // act
            var media = await mediaBackend.GetFull(mediaId, default);

            // assert
            media.Should().NotBeNull($"system icon '{idValue}' must be seeded by MediaDbInitializer");
            media!.ContentType.Should().Be("image/png", $"system icon '{idValue}' must be seeded as PNG");
            media.BlobId.Should().EndWith(".png", $"system icon '{idValue}' blob must end with .png");
        }
    }
}
