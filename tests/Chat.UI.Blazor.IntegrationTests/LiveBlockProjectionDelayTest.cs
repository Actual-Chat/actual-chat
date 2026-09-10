using ActualChat.Hashing;
using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using Bunit;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

public sealed class LiveBlockProjectionDelayTest(ITestOutputHelper @out)
    : AppHostTestBase("live-block-projection-delay", @out)
{
    [Fact]
    public async Task ConversationShouldKeepLivePresentationBeforeBlockStateArrives()
    {
        // arrange
        await using var appHost = await NewAppHost(o => o with {
            ConfigureServices = (_, services) =>
                services.AddFusion().AddService<LiveSessionUI, DelayedLiveSessionUI>(ServiceLifetime.Scoped),
        });
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob().WaitAsync(TimeSpan.FromSeconds(5));
        var (chat, _) = await tester.CreateAndGetChat(true, "pending-live-block-state-test")
            .WaitAsync(TimeSpan.FromSeconds(5));
        await ComputedTest.When(async ct => {
            var range = await tester.Chats.GetIdRange(tester.Session, chat.Id, ct);
            range.Start.Should().BePositive();
        }, TimeSpan.FromSeconds(10));
        var author = (await tester.GetOwnAuthor(chat.Id)).Require();
        var liveBackend = appHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, AuthorId.New(chat.Id, 777_080),
            null, true, true, CancellationToken.None);
        var entry = await tester.CreateTextEntry(chat.Id, "live tail");
        await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s", EndEntryLid = entry.LocalId, MessageCount = 1,
        }, CancellationToken.None);
        var liveSessionUI = (DelayedLiveSessionUI)tester.ScopedAppServices.GetRequiredService<LiveSessionUI>();
        var liveBlockUI = tester.ScopedAppServices.GetRequiredService<LiveBlockUI>();
        Conversation live = null!;
        await ComputedTest.When(async ct => {
            live = (await liveSessionUI.GetConversation(chat.Id, ct)).Require();
        }, TimeSpan.FromSeconds(5));
        var chatAudioUI = tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var chatUI = tester.ScopedAppServices.GetRequiredService<ChatUI>();
        await chatAudioUI.SetListeningState(chat.Id, true);
        using (Invalidation.Begin())
            _ = chatAudioUI.GetState(chat.Id);
        chatUI.SelectChatOnNavigation(chat.Id);
        var idRange = await tester.Chats.GetIdRange(tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        var sendingMessages = tester.ScopedAppServices.GetRequiredService<SendingMessages>();
        var pending = sendingMessages.GetSendingMessages(chat.Id).ChatSendingMessages;
        pending.AddSendingMessage(new SendingMessage(
            Guid.NewGuid().ToString(), "", chat.Id, null, tester.AppServices.Clocks().SystemClock.Now,
            "pending tail", HashString.None, null, () => { }));

        await ComputedTest.When(async ct => {
            (await liveSessionUI.AmIInLiveConversation(chat.Id, ct)).Should().BeTrue();

            // act
            var liveBlock = await liveBlockUI.GetBlock(chat.Id, ct);
            var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);

            // assert
            liveBlock.Should().BeOfType<OpenLiveBlock>()
                .Which.Should().Be(new OpenLiveBlock(live.Id, true, 0));
            var block = items.Items.OfType<ExpandedConversationMessage>().Should().ContainSingle().Subject;
            block.Items.OfType<LiveConversationHeader>().Should().ContainSingle();
            block.Items.SelectMany(i => i.GetLeafMessages()).OfType<ChatEntryMessage>()
                .Should().Contain(m => m.Id == idRange.End && m.Entry.SendingTag != null);
            block.Items[^1].Should().BeOfType<LiveConversationFooter>();
        }, TimeSpan.FromSeconds(5));
        tester.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var chatContext = new ChatContext(tester.ScopedAppServices.GetRequiredService<AppUIHub>(), chat);
        var card = tester.Render<ConversationMessageView>(p => p
            .Add(x => x.Message, new ConversationMessage(live))
            .Add(x => x.ChatContext, chatContext));
        card.WaitForAssertion(() => card.FindAll(".conversation-message.live.joined").Should().ContainSingle());
        var header = tester.Render<LiveConversationHeaderView>(p => p
            .Add(x => x.Header, new LiveConversationHeader(live))
            .Add(x => x.ChatContext, chatContext));
        header.WaitForAssertion(() => header.FindAll(".c-lc-expand").Should().ContainSingle());

        // act
        liveSessionUI.IsBlockStateDelayed.Value = false;
        await ComputedTest.When(async ct => {
            (await liveSessionUI.GetBlockState(chat.Id, ct)).Should().NotBeNull();
            (await liveBlockUI.GetBlock(chat.Id, ct)).Should().BeOfType<OpenLiveBlock>()
                .Which.HasAttended.Should().BeTrue();
        }, TimeSpan.FromSeconds(5));
        await chatAudioUI.SetListeningState(chat.Id, false);
        using (Invalidation.Begin())
            _ = chatAudioUI.GetState(chat.Id);

        // assert
        await ComputedTest.When(async ct => {
            (await liveSessionUI.AmIInLiveConversation(chat.Id, ct)).Should().BeFalse();
            (await liveBlockUI.GetBlock(chat.Id, ct)).Should().BeOfType<OpenLiveBlock>()
                .Which.HasAttended.Should().BeTrue();
        }, TimeSpan.FromSeconds(5));
        card.WaitForAssertion(() => card.FindAll(".conversation-message.live.joined").Should().ContainSingle());
    }

    public class DelayedLiveSessionUI(AppUIHub hub) : LiveSessionUI(hub)
    {
        public MutableState<bool> IsBlockStateDelayed { get; } = hub.StateFactory.NewMutable(true);

        public override async Task<LiveBlockState?> GetBlockState(ChatId chatId, CancellationToken cancellationToken)
            => await IsBlockStateDelayed.Use(cancellationToken)
                ? null
                : await base.GetBlockState(chatId, cancellationToken);
    }
}
