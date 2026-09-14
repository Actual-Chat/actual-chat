using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class OutgoingCallStatusTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Bob => field ??= AppHost.NewBlazorTester(Out);
    private BlazorTester Alice => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Bob.DisposeSilentlyAsync();
        await Alice.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task DeclineShouldSurfaceStatusThroughCallUI()
    {
        // arrange — Bob rings Alice; Bob's banner observes the status via the client-side CallUI
        await Bob.SignInAsUniqueBob();
        await Alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Bob.CreateChat(false);
        await Alice.JoinChat(chatId, inviteId);
        var bobAuthor = await Bob.GetOwnAuthor(chatId);
        var aliceAuthor = await Alice.GetOwnAuthor(chatId);
        var callUI = Bob.ScopedAppServices.AppUIHub().CallUI;
        var backend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();

        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        var cStatus = await Computed.Capture(
            () => callUI.GetCallStatus(chatId, CancellationToken.None));
        cStatus.Value.Should().Be(CallerStatus.Dialing);

        // act — Alice declines
        await backend.DeclineCall(chatId, aliceAuthor.Id, default);

        // assert — the client compute flips Dialing → NoAnswer on its own, without a fresh Capture
        // (CallerStatus doesn't distinguish "declined" from "timed out" - both read the same to the caller)
        await ComputedTest.When(async ct => {
            var status = await callUI.GetCallStatus(chatId, ct);
            status.Should().Be(CallerStatus.NoAnswer);
        }, TimeSpan.FromSeconds(10));
    }
}
