using ActualChat.Testing.Host;
using ActualLab.IO;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class McpVoiceStreamToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
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

    private static async Task<byte[]> ReadOggBytes()
    {
        var path = new FilePath(Environment.CurrentDirectory) & "data" & "soniox-tts-sample.opus";
        return await File.ReadAllBytesAsync(path.Value);
    }
}
