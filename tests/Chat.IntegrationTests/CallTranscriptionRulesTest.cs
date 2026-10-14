using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public sealed class CallTranscriptionRulesTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Caller => field ??= AppHost.NewWebClientTester(Out);
    private WebClientTester Callee => field ??= AppHost.NewWebClientTester(Out);

    protected override async Task DisposeAsync()
    {
        await Caller.DisposeSilentlyAsync();
        await Callee.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task CallWithoutTranscriptionShouldNotTranscribeTextOnlySpeaker()
    {
        // arrange - with voice streaming off, a transcript is all Callee's speech could leave
        await Caller.SignInAsUniqueBob();
        await Callee.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Caller.CreateChat(false);
        var callerAuthor = await Caller.GetOwnAuthor(chatId).Require();
        var calleeAuthor = await Callee.JoinChat(chatId, inviteId);
        var liveSessionsBackend = Caller.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var chatsBackend = Caller.AppServices.GetRequiredService<IChatsBackend>();
        var invitees = new ApiArray<AuthorId>([calleeAuthor.Id]);
        await liveSessionsBackend.StartCall(chatId, callerAuthor.Id, invitees, false, default);
        await liveSessionsBackend.AcceptCall(chatId, calleeAuthor.Id, default);
        var rules = SessionRules.Default;
        await liveSessionsBackend.SetRules(chatId, rules.WithText(false), default);
        var maxLid = await chatsBackend.GetMaxLid(chatId, false, default);

        // act
        await Callee.RecordTextOnlyUtterance(chatId, Languages.English);
        await liveSessionsBackend.SetRules(chatId, rules.WithText(true), default);
        var transcribed = await Callee.RecordVoiceEntry(chatId, Languages.English, VoiceMode.JustText, 100);

        // assert
        var calleeEntryIds = new List<ChatEntryId>();
        for (var lid = maxLid + 1; lid <= transcribed.LocalId; lid++) {
            var entry = await chatsBackend.GetEntry(ChatEntryId.New(chatId, lid), default);
            if (entry?.AuthorId == calleeAuthor.Id)
                calleeEntryIds.Add(entry.Id);
        }
        calleeEntryIds.Should().Equal([transcribed.Id],
            "an utterance made while the call wasn't transcribed must leave no entry");
    }
}
