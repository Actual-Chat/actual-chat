using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class LiveConversationsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
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
        var backend = tester.AppServices.GetRequiredService<ILiveConversationsBackend>();

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
        var backend = tester.AppServices.GetRequiredService<ILiveConversationsBackend>();

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
        var backend = tester.AppServices.GetRequiredService<ILiveConversationsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, false, default);

        // act + assert
        (await backend.IsParticipant(chatId, account.Id, default)).Should().BeFalse();

        await backend.SetParticipation(chatId, account.Id, ParticipationKind.AudioListen, true, default);
        (await backend.IsParticipant(chatId, account.Id, default)).Should().BeTrue();

        await backend.SetParticipation(chatId, account.Id, ParticipationKind.AudioListen, false, default);
        (await backend.IsParticipant(chatId, account.Id, default)).Should().BeFalse();
    }
}
