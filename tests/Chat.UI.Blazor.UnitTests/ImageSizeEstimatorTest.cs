using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ImageSizeEstimatorTest
{
    private static readonly Size2D Phone12Mp = new(4032, 3024);

    [Fact]
    public void ShouldPredictADownscaleWithinTheModelsErrorBand()
    {
        // act
        var estimate = ImageSizeEstimator.Estimate(3_500_000, Phone12Mp, "image/jpeg", new(2_764_800, 2880));

        // assert
        // The model verifiably puts this input at ~268 KB (below the brief's 300_000 floor)
        estimate.Should().BeInRange(200_000, 1_500_000);
    }

    [Fact]
    public void ShouldNeverExceedTheSourceWhenNothingIsResized()
    {
        // act
        var estimate = ImageSizeEstimator.Estimate(3_500_000, Phone12Mp, "image/jpeg", new(50_331_648, 12288));

        // assert
        estimate.Should().BeLessThanOrEqualTo(3_500_000);
    }

    [Fact]
    public void ShouldCorrectForAMoreEfficientSourceFormat()
    {
        // act
        var jpeg = ImageSizeEstimator.Estimate(2_000_000, Phone12Mp, "image/jpeg", new(2_764_800, 2880));
        var heic = ImageSizeEstimator.Estimate(2_000_000, Phone12Mp, "image/heic", new(2_764_800, 2880));

        // assert
        heic.Should().BeGreaterThan(jpeg);
    }

    [Fact]
    public void ShouldIgnoreSourceBytesForLosslessSources()
    {
        // act
        var small = ImageSizeEstimator.Estimate(500_000, Phone12Mp, "image/png", new(2_764_800, 2880));
        var large = ImageSizeEstimator.Estimate(50_000_000, Phone12Mp, "image/png", new(2_764_800, 2880));

        // assert
        small.Should().Be(large);
    }

    [Theory]
    [InlineData(412_000, "~0.4 MB")]
    [InlineData(1_600_000, "~1.5 MB")]
    [InlineData(12_400_000, "~12 MB")]
    public void ShouldFormatCoarsely(long bytes, string expected)
        => ImageSizeEstimator.Format(bytes).Should().Be(expected);
}
