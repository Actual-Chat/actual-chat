using System.Threading.Channels;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class TextEntryStreamerTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatCollection.AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private TextEntryStreamer Streamer => field ??= AppHost.Services.GetRequiredService<TextEntryStreamer>();
    private IChatsBackend ChatsBackend => field ??= AppHost.Services.GetRequiredService<IChatsBackend>();
    private IAudioStreamingBackend StreamingBackend
        => field ??= AppHost.Services.GetRequiredService<IAudioStreamingBackend>();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldFinalizeEntryWithTheWholeText()
    {
        // arrange
        var (chatId, authorId) = await NewChat();

        // act
        var entry = await Stream(chatId, authorId, "Hello, ", "streamed ", "world!");

        // assert
        entry.Content.Should().Be("Hello, streamed world!");
        entry.IsContentStreaming.Should().BeFalse();
        entry.ContentStreamId.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldExposeEntryAsStreamingWhileItArrives()
    {
        // arrange
        var (chatId, authorId) = await NewChat();
        var chunks = Channel.CreateUnbounded<string>();
        var cts = new CancellationTokenSource(WaitTimeout.Debuggable());

        // act
        var streamTask = Streamer.Stream(chatId, authorId, chunks.Reader.ReadAllAsync(cts.Token), cts.Token);
        await chunks.Writer.WriteAsync("Partial text", cts.Token);

        // assert - the entry is visible with a content stream and no content of its own
        var streaming = await WhenEntryAppears(chatId, authorId, cts.Token);
        streaming.IsContentStreaming.Should().BeTrue();
        streaming.ContentStreamId.Should().NotBeEmpty();
        streaming.Content.Should().BeEmpty();

        // act
        chunks.Writer.Complete();
        var entry = await streamTask;

        // assert
        entry.Content.Should().Be("Partial text");
        entry.IsContentStreaming.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldPublishStreamReadersCanFollow()
    {
        // arrange
        var (chatId, authorId) = await NewChat();
        var chunks = Channel.CreateUnbounded<string>();
        var cts = new CancellationTokenSource(WaitTimeout.Debuggable());
        var streamTask = Streamer.Stream(chatId, authorId, chunks.Reader.ReadAllAsync(cts.Token), cts.Token);
        await chunks.Writer.WriteAsync("One ", cts.Token);
        var streaming = await WhenEntryAppears(chatId, authorId, cts.Token);

        // act - subscribe through the entry's stream id, the way a reader discovers the stream
        var transcriptStream = await StreamingBackend
            .GetTranscript(StreamId.Parse(streaming.ContentStreamId), cts.Token)
            .Require();
        await chunks.Writer.WriteAsync("Two ", cts.Token);
        await chunks.Writer.WriteAsync("Three", cts.Token);
        chunks.Writer.Complete();

        var transcript = Transcript.Empty;
        await foreach (var diff in transcriptStream.ConfigureAwait(false))
            transcript = diff.ApplyTo(transcript);

        // assert
        transcript.Text.Should().Be("One Two Three");
        var entry = await streamTask;
        entry.Content.Should().Be("One Two Three");
    }

    [Fact]
    public async Task ShouldStreamMarkupThatParsesAsIncompleteUntilFinished()
    {
        // The point of streaming markup: every prefix has to render, and the finished entry has to
        // be ordinary complete markup.

        // arrange
        var (chatId, authorId) = await NewChat();
        var incompleteParser = new MarkupParser { AllowIncompleteMarkup = true };
        var parser = new MarkupParser();

        // act
        var entry = await Stream(chatId, authorId, "Spoiler: ||the ", "butler", " did it||");

        // assert - the finished entry is complete markup, with nothing left incomplete
        entry.Content.Should().Be("Spoiler: ||the butler did it||");
        var markup = parser.Parse(entry.Content);
        markup.ToReadableText().Should().NotContain("butler");
        GetIncomplete(markup).Should().BeEmpty();

        // assert - the prefix a reader saw mid-stream already hides the spoiler
        var midStream = incompleteParser.Parse("Spoiler: ||the butler");
        midStream.ToReadableText().Should().NotContain("butler");
        GetIncomplete(midStream).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ShouldFinalizeEntryWhenTheProducerFails()
    {
        // A producer that dies must not leave an entry streaming forever.

        // arrange
        var (chatId, authorId) = await NewChat();
        var cts = new CancellationTokenSource(WaitTimeout.Debuggable());

        // act
        var streamTask = Streamer.Stream(chatId, authorId, FailingChunks(), cts.Token);
        await streamTask.SilentAwait();

        // assert
        var entry = await WhenEntryAppears(chatId, authorId, cts.Token);
        await ComputedTest.When(async ct => {
            var current = await ChatsBackend.GetEntry(entry.Id, ct);
            current!.IsContentStreaming.Should().BeFalse();
        }, WaitTimeout);
        return;

        static async IAsyncEnumerable<string> FailingChunks() {
            yield return "Started";
            await Task.Yield();
            throw new InvalidOperationException("Producer failed");
        }
    }

    [Fact]
    public async Task ShouldPostThroughThePublicApi()
    {
        // arrange
        var (chatId, _) = await NewChat();

        // act
        var entry = await StreamViaApi(chatId, null, "Posted ", "in ", "pieces");

        // assert
        entry.Content.Should().Be("Posted in pieces");
        entry.IsContentStreaming.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldStreamEditOfARecentEntry()
    {
        // arrange
        var (chatId, _) = await NewChat();
        var posted = await Tester.CreateTextEntry(chatId, "Original");

        // act
        var entry = await StreamViaApi(chatId, posted.Id.LocalId, "Edited ", "text");

        // assert
        entry.Id.LocalId.Should().Be(posted.Id.LocalId);
        entry.Content.Should().Be("Edited text");
        entry.IsContentStreaming.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldRejectEditingSomeoneElsesEntry()
    {
        // arrange
        var (chatId, _) = await NewChat(isPublic: true);
        var posted = await Tester.CreateTextEntry(chatId, "Alice's message");
        await using var bob = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();
        await bob.Commander.Call(new Authors_Join(bob.Session, chatId, null));

        // act
        var stream = RpcStream.New(new[] { "Hijacked" }.ToAsyncEnumerable());
        var streamEntry = () => bob.Chats.StreamEntry(
            bob.Session, chatId, posted.Id.LocalId, stream, CancellationToken.None);

        // assert
        await streamEntry.Should().ThrowAsync<Exception>();
        var current = await ChatsBackend.GetEntry(posted.Id, CancellationToken.None);
        current!.Content.Should().Be("Alice's message");
    }

    [Fact]
    public async Task ShouldApplyStaleEditAsOneOrdinaryUpdate()
    {
        // Past MaxStreamingEditAge the edit still succeeds, but must not re-animate a settled
        // message - so the entry never enters the streaming state.

        // arrange
        var (chatId, _) = await NewChat();
        var posted = await Tester.CreateTextEntry(chatId, "Old message");
        await BackdateEntry(posted.Id, Constants.Chat.MaxStreamingEditAge + TimeSpan.FromMinutes(5));
        var wasStreaming = false;

        // act
        var entryTask = StreamViaApi(chatId, posted.Id.LocalId, "Rewritten ", "text");
        while (!entryTask.IsCompleted) {
            var current = await ChatsBackend.GetEntry(posted.Id, CancellationToken.None);
            wasStreaming |= current?.IsContentStreaming ?? false;
            await Task.Delay(20);
        }
        var entry = await entryTask;

        // assert
        entry.Content.Should().Be("Rewritten text");
        entry.IsContentStreaming.Should().BeFalse();
        wasStreaming.Should().BeFalse();
    }

    // Private methods

    private async Task<(ChatId ChatId, AuthorId AuthorId)> NewChat(bool isPublic = false)
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublic);
        var author = await Tester.GetOwnAuthor(chatId).Require();
        return (chatId, author.Id);
    }

    private Task<ChatEntry> Stream(ChatId chatId, AuthorId authorId, params string[] chunks)
    {
        var cts = new CancellationTokenSource(WaitTimeout.Debuggable());
        return Streamer.Stream(chatId, authorId, chunks.ToAsyncEnumerable(), cts.Token);
    }

    private Task<ChatEntry> StreamViaApi(ChatId chatId, long? localId, params string[] chunks)
    {
        var cts = new CancellationTokenSource(WaitTimeout.Debuggable());
        var stream = RpcStream.New(chunks.ToAsyncEnumerable());
        return Tester.Chats.StreamEntry(Tester.Session, chatId, localId, stream, cts.Token);
    }

    private async Task BackdateEntry(ChatEntryId entryId, TimeSpan age)
    {
        var commander = AppHost.Services.Commander();
        var beginsAt = AppHost.Services.Clocks().SystemClock.Now - age;
        await commander.Call(new ChatsBackend_ChangeEntry(
            entryId,
            null,
            Change.Update(new ChatEntryDiff { BeginsAt = beginsAt })));
    }

    private async Task<ChatEntry> WhenEntryAppears(
        ChatId chatId,
        AuthorId authorId,
        CancellationToken cancellationToken)
    {
        ChatEntry entry = null!;
        await ComputedTest.When(async ct => {
            var maxLid = await ChatsBackend.GetMaxLid(chatId, false, ct);
            var found = await ChatsBackend.GetEntry(ChatEntryId.New(chatId, maxLid), ct);
            found.Should().NotBeNull();
            found!.AuthorId.Should().Be(authorId);
            entry = found;
        }, WaitTimeout);
        return entry;
    }

    private static List<Markup> GetIncomplete(Markup markup)
    {
        var items = new List<Markup>();
        Collect(markup);
        return items;

        void Collect(Markup m) {
            if (m.IsIncomplete)
                items.Add(m);
            switch (m) {
            case MarkupSeq seq:
                foreach (var item in seq.Items)
                    Collect(item);
                break;
            case StylizedMarkup stylized:
                Collect(stylized.Content);
                break;
            case ParagraphMarkup paragraph:
                Collect(paragraph.Content);
                break;
            }
        }
    }
}
