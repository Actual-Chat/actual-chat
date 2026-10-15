using ActualChat.Localization;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Pages;
using Bunit;
using Bunit.TestDoubles;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class NonContactFooterTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExhaustedIntroductionShouldExplainTheWaitAndPreserveTheRecordingStop(bool isRecording)
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var alice = await tester.SignInAsUniqueAlice();
        await using var bobTester = AppHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        var chatId = PeerChatId.New(alice.Id, bob.Id);
        for (var i = 0; i < Constants.Chat.NonContactPeerMessageLimit; i++)
            await tester.CreateTextEntry(chatId, "Hello");

        var hub = tester.ScopedAppServices.AppUIHub();
        var chat = await TestWait.When(async ct => {
            var current = await hub.Chats.Get(tester.Session, chatId, ct);
            current!.Rules.HasReachedPeerMessageLimit.Should().BeTrue();
            return current;
        });
        tester.ComponentFactories.AddStub<RecorderToggle>();
        var model = new ChatPage.FooterModel {
            CanPost = true,
            HasReachedPeerMessageLimit = chat.Rules.HasReachedPeerMessageLimit,
            IsRecording = isRecording,
        };

        // act
        var footer = tester.Render<ChatFooter>(p => p
            .AddCascadingValue(new ChatContext(hub, chat))
            .Add(x => x.Model, model));

        // assert
        footer.Find(".chat-footer.readonly").TextContent.Should()
            .Contain(hub.StringLocalizer.ChatFooter_WaitForContact);
        footer.FindComponents<ChatMessageEditor>().Should().BeEmpty();
        footer.FindComponents<Stub<RecorderToggle>>().Count.Should().Be(isRecording ? 1 : 0);
    }
}
