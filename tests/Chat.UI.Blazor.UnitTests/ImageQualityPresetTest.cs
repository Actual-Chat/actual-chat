using System.Text.Json;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ImageQualityPresetTest
{
    [Fact]
    public void ReencodingPresetsShouldRequestMainAndEstimateOutputs()
    {
        // act
        var uhd4K = ImageQualityPreset.Uhd4K.ToRequest()!;
        var fullHd = ImageQualityPreset.FullHd.ToRequest()!;

        // assert
        uhd4K.Outputs.Should().Equal(ImageOutputSpec.Main(3840), ImageOutputSpec.Estimate(1920));
        fullHd.Outputs.Should().Equal(ImageOutputSpec.Main(1920), ImageOutputSpec.Estimate(3840));
    }

    [Fact]
    public void OriginalPresetsShouldPassFilesThroughUnlessAbove8K()
    {
        // act
        var original = ImageQualityPreset.Original.ToRequest().Outputs.Single();
        var originalWithExif = ImageQualityPreset.OriginalWithExif.ToRequest().Outputs.Single();

        // assert
        original.Should().Be(new ImageOutputSpec("main", 7680, "passthrough", true, 7680));
        originalWithExif.Should().Be(new ImageOutputSpec("main", 7680, "passthrough", false, 7680));
        ImageQualityPreset.Original.GetMaxSize().Should().BeNull();
        ImageQualityPreset.Uhd4K.GetMaxSize().Should().Be(3840);
    }

    [Fact]
    public void RequestShouldSerializeToTheShapeTheWorkerReads()
    {
        // act
        var json = JsonSerializer.Serialize(
            ImageQualityPreset.FullHd.ToRequest(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // assert
        json.Should().Be(
            """{"outputs":[{"kind":"main","maxSize":1920,"codec":"auto","stripMetadata":true,"maxPassthroughSize":null},{"kind":"estimate","maxSize":3840,"codec":"auto","stripMetadata":true,"maxPassthroughSize":null}]}""");
    }

    [Fact]
    public void DefaultPresetShouldBeUhd4K()
        => default(ImageQualityPreset).Should().Be(ImageQualityPreset.Uhd4K);
}
