using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class UploadSessionProgressTest
{
    [Theory]
    [InlineData(UploadStage.New, 0d, 0d)]
    [InlineData(UploadStage.ClientProcessing, 45d, 0d)]
    [InlineData(UploadStage.Uploading, 0d, 0d)]
    [InlineData(UploadStage.Uploading, 1d, 0.01d)]
    [InlineData(UploadStage.Uploading, 45d, 0.45d)]
    [InlineData(UploadStage.Uploading, 100d, 1d)]
    [InlineData(UploadStage.Uploading, 150d, 1d)]
    [InlineData(UploadStage.Uploaded, 0d, 1d)]
    [InlineData(UploadStage.ServerProcessing, 30d, 1d)]
    [InlineData(UploadStage.Completed, 0d, 1d)]
    public void UploadedFractionScalesStagePercent(UploadStage stage, double stageProgress, double expected)
    {
        // act
        var fraction = new UploadSessionProgress(stage, stageProgress).UploadedFraction;

        // assert
        fraction.Should().BeApproximately(expected, 1e-9);
    }
}
