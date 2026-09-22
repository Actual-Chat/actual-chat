using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ChatEntryStreamsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatCollection.AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private IChatsBackend ChatsBackend => field ??= AppHost.Services.GetRequiredService<IChatsBackend>();
    private ChatEntryStreams Streams
        => field ??= (ChatEntryStreams)AppHost.Services.GetRequiredService<IChatEntryStreamsBackend>();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldFillTheEntryAcrossCalls()
    {
        // arrange
        var chatId = await NewChat();

        // act
        var stream = await Tester.Chats.StartEntryStream(Tester.Session, chatId, null, default);
        stream = await Append(stream, "Hello, ");
        stream = await Append(stream, "streamed ");
        stream = await Append(stream, "world!");
        var finished = await Tester.Chats.FinishEntryStream(Tester.Session, stream.Id, default);

        // assert
        finished.IsCompleted.Should().BeTrue();
        finished.Offset.Should().Be("Hello, streamed world!".Length);
        var entry = await ChatsBackend.GetEntry(finished.EntryId, default);
        entry!.Content.Should().Be("Hello, streamed world!");
        entry.IsContentStreaming.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldExposeTheEntryAsStreamingUntilFinished()
    {
        // arrange
        var chatId = await NewChat();

        // act
        var stream = await Tester.Chats.StartEntryStream(Tester.Session, chatId, null, default);

        // assert - the entry is there from the first call, so a watcher can already follow it
        var entry = await ChatsBackend.GetEntry(stream.EntryId, default);
        entry.Should().NotBeNull();
        entry!.IsContentStreaming.Should().BeTrue();
        entry.ContentStreamId.Should().NotBeEmpty();
        entry.ContentStreamId.Should().NotBe(stream.Id.Value,
            "the lease handle must not be the backend's content stream id");

        // act
        await Tester.Chats.FinishEntryStream(Tester.Session, stream.Id, default);
    }

    [Fact]
    public async Task ShouldReportItsOwnOffsetOnAMismatchedAppend()
    {
        // A retried append (the response was lost) or one that skips ahead must not write twice.

        // arrange
        var chatId = await NewChat();
        var stream = await Tester.Chats.StartEntryStream(Tester.Session, chatId, null, default);
        stream = await Append(stream, "One");

        // act - the same append again, then one that skips ahead
        var retried = await Tester.Chats.AppendEntryStream(Tester.Session, stream.Id, 0, "One", default);
        var skipped = await Tester.Chats.AppendEntryStream(Tester.Session, stream.Id, 100, "Far", default);

        // assert
        retried.Offset.Should().Be(3);
        skipped.Offset.Should().Be(3);
        var finished = await Tester.Chats.FinishEntryStream(Tester.Session, stream.Id, default);
        var entry = await ChatsBackend.GetEntry(finished.EntryId, default);
        entry!.Content.Should().Be("One");
    }

    [Fact]
    public async Task ShouldRejectWritesFromAnotherUser()
    {
        // arrange
        var chatId = await NewChat(isPublic: true);
        var stream = await Tester.Chats.StartEntryStream(Tester.Session, chatId, null, default);
        await using var bob = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();
        await bob.Commander.Call(new Authors_Join { Session = bob.Session, ChatId = chatId });

        // act
        var append = () => bob.Chats.AppendEntryStream(bob.Session, stream.Id, 0, "Hijacked", default);
        var finish = () => bob.Chats.FinishEntryStream(bob.Session, stream.Id, default);

        // assert
        await append.Should().ThrowAsync<Exception>();
        await finish.Should().ThrowAsync<Exception>();
        var finished = await Tester.Chats.FinishEntryStream(Tester.Session, stream.Id, default);
        var entry = await ChatsBackend.GetEntry(finished.EntryId, default);
        entry!.Content.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldAnswerARepeatedFinishWithTheSameResult()
    {
        // arrange
        var chatId = await NewChat();
        var stream = await Tester.Chats.StartEntryStream(Tester.Session, chatId, null, default);
        stream = await Append(stream, "Done");

        // act
        var first = await Tester.Chats.FinishEntryStream(Tester.Session, stream.Id, default);
        var second = await Tester.Chats.FinishEntryStream(Tester.Session, stream.Id, default);

        // assert
        second.Should().Be(first);
        var append = () => Tester.Chats.AppendEntryStream(Tester.Session, stream.Id, 4, "More", default);
        await append.Should().ThrowAsync<Exception>("a finished stream can't be written to");
    }

    [Fact]
    public async Task ShouldFinalizeAnAbandonedStream()
    {
        // A producer that dies between calls must leave a readable message, not one that
        // streams forever.

        // arrange
        var chatId = await NewChat();
        var idleTimeout = Streams.IdleTimeout;
        Streams.IdleTimeout = TimeSpan.FromSeconds(2);
        try {
            // act - start, append once, then never call again
            var stream = await Tester.Chats.StartEntryStream(Tester.Session, chatId, null, default);
            await Append(stream, "Half a thought");

            // assert
            await ComputedTest.When(async ct => {
                var entry = await ChatsBackend.GetEntry(stream.EntryId, ct);
                entry!.IsContentStreaming.Should().BeFalse();
                entry.Content.Should().Be("Half a thought");
            }, WaitTimeout);
        }
        finally {
            Streams.IdleTimeout = idleTimeout;
        }
    }

    [Fact]
    public async Task ShouldRejectStartingInAMaintainedChat()
    {
        // arrange
        await using var admin = AppHost.NewWebClientTester(Out);
        await admin.SignInAsUniqueBobAdmin();
        var chatId = await NewChat();
        await SetMaintenance(admin, chatId, true);
        try {
            // act
            var start = () => Tester.Chats.StartEntryStream(Tester.Session, chatId, null, default);

            // assert
            await start.Should().ThrowAsync<Exception>();
        }
        finally {
            await SetMaintenance(admin, chatId, false);
        }
    }

    [Fact]
    public async Task ShouldMarkAnApiKeyStreamAsViaApi()
    {
        // arrange
        var chatId = await NewChat();
        var apiKey = await Tester.Commander.Call(
            new Accounts_CreateApiKey { Session = Tester.Session, Name = "test" });
        var apiSession = new Session(apiKey);

        // act
        var stream = await Tester.Chats.StartEntryStream(apiSession, chatId, null, default);
        await Tester.Chats.AppendEntryStream(apiSession, stream.Id, 0, "From a bot", default);
        var finished = await Tester.Chats.FinishEntryStream(apiSession, stream.Id, default);

        // assert
        var entry = await ChatsBackend.GetEntry(finished.EntryId, default);
        entry!.Content.Should().Be("From a bot");
        entry.IsViaApi.Should().BeTrue();
    }

    // Private methods

    private async Task<ChatId> NewChat(bool isPublic = false)
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublic);
        return chatId;
    }

    private async Task<ChatEntryStream> Append(ChatEntryStream stream, string text)
        => await Tester.Chats.AppendEntryStream(Tester.Session, stream.Id, stream.Offset, text, default);

    private static async Task SetMaintenance(IWebTester admin, ChatId chatId, bool isEnabled)
        => await admin.Commander.Call(new Chats_SetMaintenance {
            Session = admin.Session, ChatId = chatId, IsEnabled = isEnabled,
        });
}
