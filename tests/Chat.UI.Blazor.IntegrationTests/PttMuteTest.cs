using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class PttMuteTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task AMutedChatShouldLeaveTheArmedSetButKeepItsConsent()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var chatId = await ArmPttChat(tester, hub);
        var chatAudioUI = hub.ChatAudioUI;
        (await chatAudioUI.GetPttChatIds(CancellationToken.None)).Should().Equal(chatId);
        (await chatAudioUI.GetMutedPttChatIds(CancellationToken.None)).Should().BeEmpty();

        // act
        var now = hub.Clocks.ServerClock.Now;
        await hub.UserSettingsUI.UserPttSettings()
            .Update(x => x.WithPttChatMuted(chatId, now, now + TimeSpan.FromHours(1)));

        // assert
        (await chatAudioUI.GetPttChatIds(CancellationToken.None)).Should().BeEmpty("a muted chat is inert");
        (await chatAudioUI.GetMutedPttChatIds(CancellationToken.None)).Should().Equal(chatId);
        (await chatAudioUI.GetConsentedPttChatIds(CancellationToken.None)).Should().ContainSingle()
            .Which.Should().Be(chatId, "muting must not read as leaving PTT");
    }

    [Fact]
    public async Task ALapsedMuteShouldReArmTheChatByItself()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var chatId = await ArmPttChat(tester, hub);
        var now = hub.Clocks.ServerClock.Now;
        var mutedUntil = now + TimeSpan.FromSeconds(2);
        await hub.UserSettingsUI.UserPttSettings().Update(x => x.WithPttChatMuted(chatId, now, mutedUntil));
        (await hub.ChatAudioUI.GetPttChatIds(CancellationToken.None)).Should().BeEmpty();

        // act: nothing writes the settings; only the deadline passes
        var cArmed = await Computed.Capture(() => hub.ChatAudioUI.GetPttChatIds(CancellationToken.None));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10).Debuggable());
        cArmed = await cArmed.When(x => x.Contains(chatId), cts.Token);

        // assert
        cArmed.Value.Should().Equal(chatId);
        (await hub.ChatAudioUI.GetMutedPttChatIds(CancellationToken.None)).Should().BeEmpty();
    }

    // Private methods

    private static async Task<ChatId> ArmPttChat(BlazorTester tester, AppUIHub hub)
    {
        var (chatId, _) = await tester.CreateChat(true);
        var chat = await tester.AppServices.Commander().Call(new ChatsBackend_Change(
            chatId, null, Change.Update(new ChatDiff { PttEnabledAt = (Moment?)Moment.EpochStart })));
        await hub.UserSettingsUI.UserPttSettings().Update(x => x.WithPttChat(chatId, chat.PttEnabledAt!.Value));
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(true);
        return chatId;
    }
}
