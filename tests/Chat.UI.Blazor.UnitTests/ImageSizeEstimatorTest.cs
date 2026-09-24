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
        // Verified model output for this exact input is 268_193; ±1% keeps the assertion tied
        // to a resize-branch value instead of a range the recode/pixels-only branches also hit
        estimate.Should().BeInRange(265_511, 270_875);
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
    public void ShouldCorrectAnUnresizedRecodeForAMoreEfficientSourceFormat()
    {
        // act
        var jpeg = ImageSizeEstimator.Estimate(2_000_000, Phone12Mp, "image/jpeg", new(50_331_648, 12288));
        var heic = ImageSizeEstimator.Estimate(2_000_000, Phone12Mp, "image/heic", new(50_331_648, 12288));

        // assert
        heic.Should().BeGreaterThan(jpeg);
    }

    [Fact]
    public void ShouldGoAboveTheSourceBytesForALowBppRecode()
    {
        // act
        var estimate = ImageSizeEstimator.Estimate(500_000, Phone12Mp, "image/heic", new(12_582_912, 6144));

        // assert
        estimate.Should().BeGreaterThan(500_000);
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
    [InlineData(40_000, "~0.1MB")]
    [InlineData(412_000, "~0.4MB")]
    [InlineData(626_000, "~0.6MB")]
    [InlineData(1_600_000, "~1.5MB")]
    [InlineData(12_400_000, "~12MB")]
    public void ShouldFormatCoarsely(long bytes, string expected)
        => ImageSizeEstimator.Format(bytes, isApprox: true).Should().Be(expected);

    [Theory]
    [InlineData(626_000, "0.6MB")]
    [InlineData(478_000, "0.5MB")]
    [InlineData(4_700_000, "4.5MB")]
    public void ShouldFormatExactSizesInMbWithoutSpace(long bytes, string expected)
        => ImageSizeEstimator.Format(bytes, isApprox: false).Should().Be(expected);
}
