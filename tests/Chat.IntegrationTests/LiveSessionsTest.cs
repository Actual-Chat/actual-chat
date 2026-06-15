using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class LiveSessionsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task TranscriptionConversationStartsThenMarksClosing()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        author.Should().NotBeNull();
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

        // assert
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.TranscriptionOn.Should().BeTrue();
        live.AuthorIds.Should().Contain(author.Id);
        live.IsClosing.Should().BeFalse();

        // act — no live streams remain, so close detection marks it closing (the flow finalizes it)
        await backend.OnStreamsChanged(chatId, default);

        // assert
        live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue();

        // act
        await backend.Close(chatId, default);

        // assert
        (await backend.Get(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task PhoneModeConversationVanishesOnClose()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.OnStreamRegistered(chatId, author!.Id, null, false, default);

        // assert
        (await backend.Get(chatId, default)).Should().NotBeNull();

        // act — phone-mode close removes the block immediately
        await backend.OnStreamsChanged(chatId, default);

        // assert
        (await backend.Get(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task ParticipationIsTracked()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, false, default);

        // act + assert
        (await backend.IsParticipant(chatId, account.Id, default)).Should().BeFalse();

        await backend.SetParticipation(chatId, account.Id, ParticipationKind.AudioListen, true, default);
        (await backend.IsParticipant(chatId, account.Id, default)).Should().BeTrue();

        await backend.SetParticipation(chatId, account.Id, ParticipationKind.AudioListen, false, default);
        (await backend.IsParticipant(chatId, account.Id, default)).Should().BeFalse();
    }

    [Fact]
    public async Task ClosingTransitionStampsClosingAt()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

        // act — no live streams remain, so it transitions to closing and stamps ClosingAt
        await backend.OnStreamsChanged(chatId, default);

        // assert — ClosingAt drives the self-heal timeout that vanishes a flow-less conversation
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue();
        live.ClosingAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RejoinClearsClosingState()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamsChanged(chatId, default);
        (await backend.Get(chatId, default))!.IsClosing.Should().BeTrue();

        // act — a stream registers again before finalization
        await backend.OnStreamRegistered(chatId, author.Id, null, true, default);

        // assert — re-open clears both the closing flag and the timeout stamp
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeFalse();
        live.ClosingAt.Should().BeNull();
    }
}
