using System.Text.Json;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ImageQualityPresetTest
{
    [Fact]
    public void DefaultPresetShouldBe12Mpx()
        => default(ImageQualityPreset).Should().Be(ImageQualityPreset.Mpx12);

    [Theory]
    [InlineData(ImageQualityPreset.Mpx50, 50_331_648, 12288)]
    [InlineData(ImageQualityPreset.Mpx12, 12_582_912, 6144)]
    [InlineData(ImageQualityPreset.Mpx3, 2_764_800, 2880)]
    public void ReEncodingPresetsShouldCarryTheirBudget(ImageQualityPreset preset, int maxPixels, int maxLongSide)
    {
        var budget = preset.GetBudget();
        budget.MaxPixels.Should().Be(maxPixels);
        budget.MaxLongSide.Should().Be(maxLongSide);
    }

    [Theory]
    [InlineData(ImageQualityPreset.Original)]
    [InlineData(ImageQualityPreset.OriginalWithExif)]
    public void OriginalPresetsShouldHaveNoBudget(ImageQualityPreset preset)
    {
        var budget = preset.GetBudget();
        budget.MaxPixels.Should().BeNull();
        budget.MaxLongSide.Should().BeNull();
    }

    [Fact]
    public void OriginalWithExifShouldKeepMetadata()
        => ImageQualityPreset.OriginalWithExif.ToRequest().Outputs[0].StripMetadata.Should().BeFalse();

    [Fact]
    public void OriginalShouldStripMetadataWithoutReEncoding()
    {
        var spec = ImageQualityPreset.Original.ToRequest().Outputs[0];
        spec.StripMetadata.Should().BeTrue();
        spec.Codec.Should().Be("passthrough");
    }

    [Fact]
    public void ReEncodingPresetShouldRequestMainAndPlaceholder()
    {
        var outputs = ImageQualityPreset.Mpx12.ToRequest().Outputs;
        outputs.Select(o => o.Kind).Should().Contain("main");
        outputs.Select(o => o.Kind).Should().Contain("placeholder");
    }

    [Fact]
    public void RequestShouldSerializeToTheShapeTheWorkerReads()
    {
        // act
        var json = JsonSerializer.Serialize(
            ImageQualityPreset.Mpx12.ToRequest(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // assert
        json.Should().Be(
            """{"outputs":[{"kind":"main","maxPixels":12582912,"maxLongSide":6144,"codec":"auto","stripMetadata":true,"maxPassthroughPixels":null},{"kind":"placeholder","maxPixels":null,"maxLongSide":null,"codec":"placeholder","stripMetadata":true,"maxPassthroughPixels":null}]}""");
    }
}
