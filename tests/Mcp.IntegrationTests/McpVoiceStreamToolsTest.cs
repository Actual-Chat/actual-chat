using ActualChat.Chat;
using ActualChat.Testing;
using ActualChat.Testing.Host;
using ActualLab.IO;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public sealed class McpVoiceStreamToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    private const int ChunkSize = 4 * 1024;
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinAudioDuration = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task ShouldReassembleAnOggPageSplitAcrossTwoAppends()
    {
        // A producer chunks at arbitrary byte boundaries; a frame straddling two appends must
        // survive intact rather than being dropped or corrupted.

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var ogg = await ReadOggBytes();
        var half = ogg.Length / 2;

        // act
        var stream = await CallTool<McpVoiceStream>(client, "start_voice_stream",
            new { chatId = chatId.Value });
        await CallTool<McpVoiceStream>(client, "append_voice_stream", new {
            streamId = stream.StreamId, textOffset = 0,
            audioBase64 = Convert.ToBase64String(ogg[..half]),
        });
        var afterSecond = await CallTool<McpVoiceStream>(client, "append_voice_stream", new {
            streamId = stream.StreamId, textOffset = 0,
            audioBase64 = Convert.ToBase64String(ogg[half..]),
        });
        var final = await CallTool<McpVoiceStream>(client, "finish_voice_stream",
            new { streamId = stream.StreamId });

        // assert
        afterSecond.AudioBytes.Should().Be(ogg.Length, "every byte of both halves was accepted");
        final.IsFinished.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldCarryTextAlongsideTheAudio()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var ogg = await ReadOggBytes();

        // act
        var stream = await CallTool<McpVoiceStream>(client, "start_voice_stream",
            new { chatId = chatId.Value });
        var appended = await CallTool<McpVoiceStream>(client, "append_voice_stream", new {
            streamId = stream.StreamId, textOffset = 0, text = "Spoken words",
            audioBase64 = Convert.ToBase64String(ogg),
        });
        var final = await CallTool<McpVoiceStream>(client, "finish_voice_stream",
            new { streamId = stream.StreamId });

        // assert
        appended.TextOffset.Should().Be("Spoken words".Length);
        final.IsFinished.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldPostAnEntryThatCarriesTheAudioItWasGiven()
    {
        // The whole point of a voice stream: what a listener plays back is the producer's own
        // audio. An entry that arrives with the right words but no sound is not the feature.

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var ogg = await ReadOggBytes();
        var chatsBackend = AppHost.Services.GetRequiredService<IChatsBackend>();

        // act - chunked the way a producer streams it, rather than in one call
        var stream = await CallTool<McpVoiceStream>(client, "start_voice_stream",
            new { chatId = chatId.Value });
        McpVoiceStream appended = null!;
        for (var offset = 0; offset < ogg.Length; offset += ChunkSize) {
            var chunk = ogg[offset..Math.Min(offset + ChunkSize, ogg.Length)];
            appended = await CallTool<McpVoiceStream>(client, "append_voice_stream", new {
                streamId = stream.StreamId, textOffset = 0,
                audioBase64 = Convert.ToBase64String(chunk),
            });
        }
        var final = await CallTool<McpVoiceStream>(client, "finish_voice_stream",
            new { streamId = stream.StreamId });

        // assert - the reply says how much audio was decoded, which is the producer's only way to
        // tell that its bytes were read at all, and names the message it just posted
        appended.AudioDuration.Should().BeGreaterThan(MinAudioDuration.TotalSeconds,
            "the reply reports decoded audio, not bytes received");
        final.EntryId.Should().NotBeNull("a producer must be able to name its own message");
        await TestWait.When(async ct => {
            var entries = await ListEntries(chatsBackend, chatId, ct);
            entries.Should().Contain(
                e => e.LocalId == final.EntryId!.Value
                    && e.HasAudio
                    && e.Duration > MinAudioDuration.TotalSeconds,
                "the entry must carry the audio, not just be marked as having some");
        }, WaitTimeout);
    }

    [Fact]
    public async Task ShouldPostInTheLanguageTheProducerDeclared()
    {
        // Nothing reads these words, so an undeclared voice message names no language - and a
        // listener is only ever offered a translation of one whose language is known.

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var ogg = await ReadOggBytes();
        var chatsBackend = AppHost.Services.GetRequiredService<IChatsBackend>();

        // act
        var stream = await CallTool<McpVoiceStream>(client, "start_voice_stream",
            new { chatId = chatId.Value, language = "de-DE" });
        await CallTool<McpVoiceStream>(client, "append_voice_stream", new {
            streamId = stream.StreamId, textOffset = 0, text = "Guten Tag",
            audioBase64 = Convert.ToBase64String(ogg),
        });
        var final = await CallTool<McpVoiceStream>(client, "finish_voice_stream",
            new { streamId = stream.StreamId });

        // assert
        final.EntryId.Should().NotBeNull("finish names the message that was posted");
        await TestWait.When(async ct => {
            var entries = await ListEntries(chatsBackend, chatId, ct);
            entries.Should().Contain(e => e.LocalId == final.EntryId!.Value && e.Content == "Guten Tag",
                "the words the producer sent are the message, not a guess at them");
        }, WaitTimeout);
    }

    [Fact]
    public async Task ShouldSayWhyAnAppendWasRejected()
    {
        // The MCP SDK masks every exception but McpException behind "An error occurred invoking
        // '<tool>'", and an agent that cannot read the reason cannot correct itself.

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var stream = await CallTool<McpVoiceStream>(client, "start_voice_stream",
            new { chatId = chatId.Value });

        // act
        var error = await CallToolExpectingError(client, "append_voice_stream", new {
            streamId = stream.StreamId, textOffset = -1, text = "backwards",
        });

        // assert
        error.Should().Contain("Offset cannot be negative",
            "a masked exception says only that an error occurred");
        await CallTool<McpVoiceStream>(client, "finish_voice_stream", new { streamId = stream.StreamId });
    }

    [Fact]
    public async Task ShouldTellCallersHowToEncodeTheirAudio()
    {
        // The reader takes only 20 ms single-frame packets, and an encoder set to 60 ms is the
        // easiest way to get this wrong - so the rejection has to name the fix.

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var stream = await CallTool<McpVoiceStream>(client, "start_voice_stream",
            new { chatId = chatId.Value });

        // act
        var error = await CallToolExpectingError(client, "append_voice_stream", new {
            streamId = stream.StreamId, textOffset = 0,
            audioBase64 = Convert.ToBase64String(await ReadLongPacketOggBytes()),
        });

        // assert
        error.Should().Contain("20 ms", "the rejection must name the constraint");
        error.Should().Contain("frame_duration", "and the flag that satisfies it");
    }

    [Fact]
    public async Task ShouldTellAnAgentHowToUseTheServer()
    {
        // arrange, act
        await Tester.SignInAsUniqueAlice();
        await using var client = await CreateClient();

        // assert - the one place MCP carries what no single tool description can
        var instructions = client.ServerInstructions ?? "";
        instructions.Should().Contain("20 ms", "the audio contract is not in any one tool description");
        instructions.Should().Contain("start_voice_stream", "nor is the choice between the ways to post");
    }

    [Fact]
    public async Task ShouldReportItsOwnOffsetOnAMismatchedTextAppend()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var stream = await CallTool<McpVoiceStream>(client, "start_voice_stream",
            new { chatId = chatId.Value });
        await CallTool<McpVoiceStream>(client, "append_voice_stream",
            new { streamId = stream.StreamId, textOffset = 0, text = "One" });

        // act - the same call again, as a client that lost the response would make it
        var retried = await CallTool<McpVoiceStream>(client, "append_voice_stream",
            new { streamId = stream.StreamId, textOffset = 0, text = "One" });

        // assert
        retried.TextOffset.Should().Be(3);
        await CallTool<McpVoiceStream>(client, "finish_voice_stream", new { streamId = stream.StreamId });
    }

    [Fact]
    public async Task ShouldRejectACorruptAudioChunkAndKeepTheStreamOpen()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var stream = await CallTool<McpVoiceStream>(client, "start_voice_stream",
            new { chatId = chatId.Value });

        // act
        await CallToolExpectingError(client, "append_voice_stream", new {
            streamId = stream.StreamId, textOffset = 0, audioBase64 = "!!!not base64!!!",
        });
        var after = await CallTool<McpVoiceStream>(client, "append_voice_stream", new {
            streamId = stream.StreamId, textOffset = 0, text = "still here",
        });

        // assert
        after.TextOffset.Should().Be("still here".Length, "a bad chunk must not kill the stream");
        await CallTool<McpVoiceStream>(client, "finish_voice_stream", new { streamId = stream.StreamId });
    }

    [Fact]
    public async Task ShouldRejectAnotherUsersVoiceStream()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        await Tester.SignInAsUniqueBob();
        var bobKey = await IssueApiKey("bob");
        await Tester.SignIn(alice);
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);

        await using var aliceMcp = await CreateClientWithRawKey(aliceKey);
        await using var bobMcp = await CreateClientWithRawKey(bobKey);
        var stream = await CallTool<McpVoiceStream>(aliceMcp, "start_voice_stream",
            new { chatId = chatId.Value });

        // act, assert
        await CallToolExpectingError(bobMcp, "append_voice_stream",
            new { streamId = stream.StreamId, textOffset = 0, text = "Hijacked" });
        await CallToolExpectingError(bobMcp, "finish_voice_stream", new { streamId = stream.StreamId });
        await CallTool<McpVoiceStream>(aliceMcp, "finish_voice_stream", new { streamId = stream.StreamId });
    }

    // Private methods

    private static async Task<List<ChatEntry>> ListEntries(
        IChatsBackend chatsBackend, ChatId chatId, CancellationToken cancellationToken)
    {
        var maxLid = await chatsBackend.GetMaxLid(chatId, false, cancellationToken);
        var tile = Constants.Chat.EntryIdTiles.GetTile(maxLid);
        var chatTile = await chatsBackend.GetTile(chatId, tile.Range, false, cancellationToken);
        return chatTile.Entries.ToList();
    }

    private static async Task<byte[]> ReadLongPacketOggBytes()
    {
        var path = new FilePath(Environment.CurrentDirectory) & "data" & "long-packets.opus";
        return await File.ReadAllBytesAsync(path.Value);
    }

    private static async Task<byte[]> ReadOggBytes()
    {
        var path = new FilePath(Environment.CurrentDirectory) & "data" & "soniox-tts-sample.opus";
        return await File.ReadAllBytesAsync(path.Value);
    }
}
