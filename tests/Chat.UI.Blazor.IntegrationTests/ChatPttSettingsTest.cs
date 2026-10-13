using System.Security;
using ActualChat.Localization;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;
using ActualChat.Users;
using Bunit;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class ChatPttSettingsTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PauseAndResumeShouldPreserveConsentMuteAndDeviceChoices(bool isMuted, bool isDeviceEnabled)
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var (chatId, _) = await tester.CreateChat(true);
        var chat = await ChangeChat(tester, chatId, new ChatDiff { PttEnabledAt = (Moment?)Moment.EpochStart });
        chat = (await hub.Chats.Get(tester.Session, chatId, default))!;
        var enabledAt = chat.PttEnabledAt!.Value;
        var now = hub.Clocks.ServerClock.Now;
        await hub.UserSettingsUI.UserPttSettings().Update(x => {
            var settings = x.WithPttChat(chatId, enabledAt);
            return isMuted
                ? settings.WithPttChatMuted(chatId, now, now + TimeSpan.FromHours(1))
                : settings;
        });
        var priorSettings = await hub.UserSettingsUI.UserPttSettings().Get();
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(isDeviceEnabled);

        // act
        chat = await ChangeChat(tester, chatId, new ChatDiff { IsPttPaused = true });

        // assert
        chat.PttEnabledAt.Should().Be(enabledAt);
        chat.ActivePttEnabledAt.Should().BeNull();
        await TestWait.When(async ct => {
            (await hub.ChatAudioUI.GetPttChatIds(ct)).Should().BeEmpty();
            (await hub.ChatAudioUI.GetMutedPttChatIds(ct)).Should().BeEmpty();
            (await hub.ChatAudioUI.GetConsentedPttChatIds(ct)).Should().Contain(chatId);
        });

        // act
        chat = await ChangeChat(tester, chatId, new ChatDiff { IsPttPaused = false });

        // assert
        chat.ActivePttEnabledAt.Should().Be(enabledAt);
        await TestWait.When(async ct => {
            var armedIds = await hub.ChatAudioUI.GetPttChatIds(ct);
            armedIds.Contains(chatId).Should().Be(isDeviceEnabled && !isMuted);
            var mutedIds = await hub.ChatAudioUI.GetMutedPttChatIds(ct);
            mutedIds.Contains(chatId).Should().Be(isDeviceEnabled && isMuted);
            (await hub.ChatAudioUI.GetExpiredPttConsentChatIds(ct)).Should().BeEmpty();
        });
        var settings = await hub.UserSettingsUI.UserPttSettings().Get();
        settings.PttChats.Should().Equal(priorSettings.PttChats);
        (await hub.ChatAudioUI.IsPttEnabledOnDevice(CancellationToken.None)).Should().Be(isDeviceEnabled);
        var bannerKind = Ptt.GetJoinBannerKind(
            settings.IsArmedIn(chatId, chat.PttEnabledAt), isDeviceEnabled, default, enabledAt);
        bannerKind.Should().Be(isDeviceEnabled ? PttJoinBannerKind.None : PttJoinBannerKind.EnableDevice);
    }

    [Fact]
    public async Task JoiningAnotherChatShouldNotPrunePausedConsent()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var (pausedId, _) = await tester.CreateChat(true);
        var pausedChat = await ChangeChat(tester, pausedId, new ChatDiff { PttEnabledAt = (Moment?)Moment.EpochStart });
        await hub.ChatAudioUI.ConsentPtt(pausedId, pausedChat.PttEnabledAt!.Value, CancellationToken.None);
        await ChangeChat(tester, pausedId, new ChatDiff { IsPttPaused = true });
        var (otherId, _) = await tester.CreateChat(true);
        var otherChat = await ChangeChat(tester, otherId, new ChatDiff { PttEnabledAt = (Moment?)Moment.EpochStart });

        // act
        await hub.ChatAudioUI.ConsentPtt(otherId, otherChat.PttEnabledAt!.Value, CancellationToken.None);

        // assert
        var consentedIds = await hub.ChatAudioUI.GetConsentedPttChatIds(CancellationToken.None);
        consentedIds.Should().BeEquivalentTo([pausedId, otherId]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonOwnersShouldSeeReadOnlyChatPttSettings(bool isPaused)
    {
        // arrange
        await using var owner = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        var (chatId, _) = await owner.CreateChat(true);
        await ChangeChat(owner, chatId, new ChatDiff { PttEnabledAt = (Moment?)Moment.EpochStart });
        if (isPaused)
            await ChangeChat(owner, chatId, new ChatDiff { IsPttPaused = true });
        await using var member = AppHost.NewBlazorTester(Out);
        await member.SignInAsUniqueAlice();
        await member.JoinChat(chatId, default);

        // act
        var availabilityTask = () => ChangeChat(member, chatId, new ChatDiff { IsPttPaused = !isPaused });

        // assert
        await availabilityTask.Should().ThrowAsync<SecurityException>();
        var chat = await owner.AppServices.GetRequiredService<IChats>().Get(owner.Session, chatId, default);
        chat!.IsPttPaused.Should().Be(isPaused);
        var hub = member.ScopedAppServices.AppUIHub();
        var host = member.RenderModalHost(hub);
        await hub.ModalUI.Show(new ChatPttSettingsModal.Model(chatId));
        await TestWait.WhenRendered(host, () => {
            var settings = host.FindComponent<ChatPttSettingsModal>();
            settings.Markup.Should().Contain(isPaused
                ? hub.StringLocalizer.Ptt_ChatPaused
                : hub.StringLocalizer.Ptt_ChatActive);
            settings.Markup.Should().Contain(hub.StringLocalizer.Ptt_ChatReadOnlyCaption);
            settings.FindComponents<Toggle>().Should().BeEmpty();
            settings.FindAll(".btn-primary").Should().BeEmpty("only owners manage chat availability");
        });
    }

    [Fact]
    public async Task EnablingChatPttShouldNotEnrolTheOwnerOrChangeTheirDeviceChoice()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(false);
        var (chatId, _) = await tester.CreateChat(true);
        var host = tester.RenderModalHost(hub);
        await hub.ModalUI.Show(new ChatPttSettingsModal.Model(chatId));
        await TestWait.WhenRendered(host, () => {
            var settings = host.FindComponent<ChatPttSettingsModal>();
            settings.FindComponents<Toggle>().Should().ContainSingle();
            settings.FindComponent<Toggle>().Instance.IsChecked.Should().BeFalse();
            settings.Markup.Should().Contain(hub.StringLocalizer.Ptt_ChatAvailability);
            settings.Markup.Should().Contain(hub.StringLocalizer.Ptt_ChatScopeCaption);
            settings.Markup.Should().NotContain(hub.StringLocalizer.Ptt_ChatReadOnlyCaption);
            settings.FindAll(".btn-primary").Should().BeEmpty();
        });

        // act
        await host.InvokeAsync(() => host.FindComponent<ChatPttSettingsModal>().Find("input").Change(true));

        // assert
        await TestWait.WhenRendered(host, () => host.FindComponent<ChatPttSettingsModal>()
            .FindComponent<Toggle>().Instance.IsChecked.Should().BeTrue());
        (await hub.ChatAudioUI.GetConsentedPttChatIds(CancellationToken.None)).Should().BeEmpty();
        (await hub.ChatAudioUI.IsPttEnabledOnDevice(CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task SettingsShouldConfirmPauseAndResumeWithoutRejoining()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var (chatId, _) = await tester.CreateChat(true);
        var chat = await ChangeChat(tester, chatId, new ChatDiff { PttEnabledAt = (Moment?)Moment.EpochStart });
        await hub.ChatAudioUI.ConsentPtt(chatId, chat.PttEnabledAt!.Value, CancellationToken.None);
        var host = tester.RenderModalHost(hub);
        await hub.ModalUI.Show(new ChatPttSettingsModal.Model(chatId));
        await TestWait.WhenRendered(host, () => host.FindComponent<ChatPttSettingsModal>()
            .FindComponent<Toggle>().Instance.IsChecked.Should().BeTrue());

        // act
        var settings = host.FindComponent<ChatPttSettingsModal>();
        var pauseTask = settings.InvokeAsync(() => settings.Find("input").Change(false));
        await TestWait.WhenRendered(host, () => host.FindComponent<ConfirmModal>()
            .Markup.Should().Contain("Members’ participation and listening preferences will be kept."));

        // assert
        (await hub.Chats.Get(tester.Session, chatId, default))!.IsPttPaused.Should().BeFalse();
        settings.FindComponents<Toggle>().Should().ContainSingle("only chat-wide availability is editable");
        settings.Find("input").HasAttribute("disabled").Should().BeTrue();
        await settings.InvokeAsync(() => settings.FindComponent<TileItem>()
            .Instance.ToggleValueChanged.InvokeAsync(false));
        host.FindComponents<ConfirmModal>().Should().ContainSingle("repeated requests cannot start another change");

        // act
        await host.InvokeAsync(() => host.FindComponent<ConfirmModal>().Find(".btn-modal:not(.btn-primary)").Click());
        await pauseTask;

        // assert
        (await hub.Chats.Get(tester.Session, chatId, default))!.IsPttPaused.Should().BeFalse();
        await TestWait.WhenRendered(host, () => {
            host.FindComponents<ConfirmModal>().Should().BeEmpty();
            settings.Find("input").HasAttribute("disabled").Should().BeFalse();
            settings.Find("label.toggle").GetAttribute("aria-checked").Should().Be("true");
        });

        // act
        pauseTask = settings.InvokeAsync(() => settings.Find(".tile-item").Click());
        await TestWait.WhenRendered(host, () => host.FindComponents<ConfirmModal>().Should().ContainSingle());
        await host.InvokeAsync(() => host.FindComponent<ConfirmModal>().Find(".btn-primary").Click());
        await pauseTask;
        await TestWait.WhenRendered(host, () => settings.Find("label.toggle")
            .GetAttribute("aria-checked").Should().Be("false"));
        await settings.InvokeAsync(() => settings.Find("input").Change(true));

        // assert
        await TestWait.WhenRendered(host, () => settings.Find("label.toggle")
            .GetAttribute("aria-checked").Should().Be("true"));
        var consentedIds = await hub.ChatAudioUI.GetConsentedPttChatIds(CancellationToken.None);
        consentedIds.Should().Contain(chatId);
        var resumedChat = await hub.Chats.Get(tester.Session, chatId, default);
        resumedChat!.PttEnabledAt.Should().Be(chat.PttEnabledAt!.Value.Floor(TimeSpan.FromMicroseconds(1)));
    }

    [Fact]
    public async Task FailedPauseShouldRestoreTheToggleAndKeepChatAvailability()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var (chatId, _) = await tester.CreateChat(true);
        await ChangeChat(tester, chatId, new ChatDiff { PttEnabledAt = (Moment?)Moment.EpochStart });
        var host = tester.RenderModalHost(hub);
        await hub.ModalUI.Show(new ChatPttSettingsModal.Model(chatId));
        await TestWait.WhenRendered(host, () => host.FindComponent<ChatPttSettingsModal>()
            .Find("label.toggle").GetAttribute("aria-checked").Should().Be("true"));
        var settings = host.FindComponent<ChatPttSettingsModal>();
        var pauseTask = settings.InvokeAsync(() => settings.Find("input").Change(false));
        await TestWait.WhenRendered(host, () => host.FindComponents<ConfirmModal>().Should().ContainSingle());

        // act
        await ChangeChat(tester, chatId, new ChatDiff { Title = "Changed while confirming pause" });
        await host.InvokeAsync(() => host.FindComponent<ConfirmModal>().Find(".btn-primary").Click());
        await pauseTask;

        // assert
        await TestWait.WhenPolled(() => {
            var result = hub.UICommander.UIActionTracker.LastResult.Value;
            result.Should().NotBeNull();
            result!.Command.Should().BeOfType<Chats_Change>();
            result.Error.Should().NotBeNull("the confirmed action used an outdated chat version");
        });
        await TestWait.WhenRendered(host, () => {
            settings.Find("label.toggle").GetAttribute("aria-checked").Should().Be("true");
            settings.Find("input").HasAttribute("disabled").Should().BeFalse();
        });
        (await hub.Chats.Get(tester.Session, chatId, default))!.ActivePttEnabledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task PausingWithoutEnablingPttShouldBeRejected()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);

        // act
        var pauseTask = () => ChangeChat(tester, chatId, new ChatDiff { IsPttPaused = true });

        // assert
        await pauseTask.Should().ThrowAsync<Exception>()
            .WithMessage("Push-to-talk must be enabled before it can be paused.");
    }

    // Private methods

    private static Task<Chat> ChangeChat(BlazorTester tester, ChatId chatId, ChatDiff diff)
    {
        var changeCmd = new Chats_Change {
            Session = tester.Session,
            ChatId = chatId,
            ExpectedVersion = null,
            Change = Change.Update(diff),
        };
        return tester.Commander.Call(changeCmd);
    }
}
