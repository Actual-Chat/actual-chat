using ActualChat.Flows;
using ActualChat.Media.Flows;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class UploadProcessingFlowTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task MissingUploadShouldFailTheFlowWithoutRetries()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var reserveMediaCmd = new Media_ReserveMedia { Session = tester.Session, Scope = "test-scope" };
        var mediaId = await tester.Commander.Call(reserveMediaCmd);
        var arguments = UploadProcessingFlow.GetArguments(UploadId.New(), mediaId);
        var flowId = FlowHub.NewId<UploadProcessingFlow>(arguments);
        var flowBackend = AppHost.Services.GetRequiredService<IFlowBackend>();

        // act
        await FlowHub.NewResumeEvent<UploadProcessingFlow>(arguments).Schedule();

        // assert
        // A retried flow stays incomplete for minutes, so it never gets here within the budget
        var flowData = await TestWait.WhenPolled<IFlowData>(async () => {
            var data = await flowBackend.TryGetData(flowId, default);
            data.Should().NotBeNull("the scheduled resume must create the flow");
            data.IsCompleted.Should().BeTrue("a missing upload is a permanent error");
            return data;
        });
        flowData.IsFailed.Should().BeTrue("the flow completes with the error, not with a result");
        var progress = await AppHost.Services.GetRequiredService<IMediaProgressBackend>().Get(mediaId, default);
        progress.Should().NotBeNull("the media is reserved");
        progress.Error.Should().NotBeNullOrEmpty("the media must show the upload failed");
    }
}
