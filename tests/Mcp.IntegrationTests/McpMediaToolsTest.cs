using ActualChat.Chat.Flows;
using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class McpMediaToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ChunkedUploadShouldProduceReadyImage()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var png = TestImages.Png(64, 48);
        var half = png.Length / 2;

        // act
        var begun = await CallTool<McpUploadStatus>(client, "begin_upload", new {
            fileName = "photo.png", contentType = "image/png", length = png.Length, chatId = chatId.Value,
        });
        var first = await CallTool<McpUploadStatus>(client, "append_upload", new {
            uploadId = begun.UploadId, offset = 0, dataBase64 = Convert.ToBase64String(png, 0, half),
        });
        var second = await CallTool<McpUploadStatus>(client, "append_upload", new {
            uploadId = begun.UploadId, offset = half, dataBase64 = Convert.ToBase64String(png, half, png.Length - half),
        });
        var finished = await CallTool<McpUploadStatus>(client, "finish_upload", new { uploadId = begun.UploadId });

        // assert
        begun.Offset.Should().Be(0);
        first.Offset.Should().Be(half);
        second.Offset.Should().Be(png.Length);
        finished.Stage.Should().Be("Ready");
        finished.Media.Should().NotBeNull();
        finished.Media!.MediaId.Should().Be(begun.MediaId);
        finished.Media.Width.Should().Be(64);
        finished.Media.Height.Should().Be(48);
        finished.Media.Url.Should().StartWith("http");
    }

    [Fact]
    public async Task AppendUploadWithStaleOffsetShouldReturnServerOffset()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var data = new byte[100];
        Random.Shared.NextBytes(data);
        var begun = await CallTool<McpUploadStatus>(client, "begin_upload", new {
            fileName = "blob.bin",
            contentType = "application/octet-stream",
            length = data.Length,
            chatId = chatId.Value,
        });
        await CallTool<McpUploadStatus>(client, "append_upload", new {
            uploadId = begun.UploadId, offset = 0, dataBase64 = Convert.ToBase64String(data, 0, 40),
        });

        // act
        var stale = await CallTool<McpUploadStatus>(client, "append_upload", new {
            uploadId = begun.UploadId, offset = 0, dataBase64 = Convert.ToBase64String(data, 0, 40),
        });
        var resumed = await CallTool<McpUploadStatus>(client, "append_upload", new {
            uploadId = begun.UploadId, offset = stale.Offset, dataBase64 = Convert.ToBase64String(data, 40, 60),
        });
        var finished = await CallTool<McpUploadStatus>(client, "finish_upload", new { uploadId = begun.UploadId });

        // assert
        stale.Offset.Should().Be(40, "a stale offset is not applied; the server's offset is returned");
        resumed.Offset.Should().Be(100);
        finished.Stage.Should().Be("Ready");
        finished.Media!.Length.Should().Be(100);
    }

    [Fact]
    public async Task AbortUploadShouldMakeAppendFail()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var begun = await CallTool<McpUploadStatus>(client, "begin_upload", new {
            fileName = "x.bin", contentType = "application/octet-stream", length = 10, chatId = chatId.Value,
        });

        // act
        await CallTool(client, "abort_upload", new { uploadId = begun.UploadId });
        var error = await CallToolExpectingError(client, "append_upload", new {
            uploadId = begun.UploadId, offset = 0, dataBase64 = Convert.ToBase64String(new byte[10]),
        });

        // assert
        error.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UploadFromUrlShouldFetchAndProcess()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();
        var png = TestImages.Png(40, 20);
        var begun = await CallTool<McpUploadStatus>(client, "begin_upload", new {
            fileName = "source.png", contentType = "image/png", length = png.Length, chatId = chatId.Value,
        });
        await CallTool<McpUploadStatus>(client, "append_upload", new {
            uploadId = begun.UploadId, offset = 0, dataBase64 = Convert.ToBase64String(png),
        });
        var source = await CallTool<McpUploadStatus>(client, "finish_upload", new { uploadId = begun.UploadId });
        var sourceUrl = source.Media!.Url.Replace("127.0.0.1", "localhost");

        // act
        var copied = await CallTool<McpUploadStatus>(client, "upload_from_url",
            new { url = sourceUrl, chatId = chatId.Value, fileName = "copy.png" });

        // assert
        copied.Stage.Should().Be("Ready");
        copied.Media!.MediaId.Should().NotBe(source.Media.MediaId);
        copied.Media.Width.Should().Be(40);
        copied.Media.FileName.Should().Be("copy.png");
    }

    [Fact]
    public async Task UploadFromUrlShouldRejectNonHttpUrl()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var client = await CreateClient();

        // act
        var error = await CallToolExpectingError(client, "upload_from_url",
            new { url = "file:///etc/passwd", chatId = chatId.Value });

        // assert
        error.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ContentListsShouldReturnIndexedMediaFilesAndLinks()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var image = await Tester.CreateImageMedia(chatId, "shot.png");
        var file = await Tester.SaveTextFile(chatId, "notes.txt", "notes");
        await Tester.CreateTextEntry(chatId, "a picture", image.Id);
        await Tester.CreateTextEntry(chatId, "a file", file);
        await Tester.CreateTextEntry(chatId, "a link https://example.net/mcp");
        await Task.Delay(TimeSpan.FromSeconds(4));
        await FlowHub.NewResumeEvent<ChatMediaIndexingFlow>(chatId.Value).Schedule();
        await FlowHub.NewResumeEvent<ChatEntryContentIndexingFlow>(chatId.Value).Schedule();
        var client = await CreateClient();

        // act
        var media = await WaitFor(
            () => CallTool<McpContentPage<McpMediaItem>>(client, "list_media", new { chatId = chatId.Value }),
            p => p.Items.Length > 0, attempts: 300);
        var files = await WaitFor(
            () => CallTool<McpContentPage<McpFileItem>>(client, "list_files", new { chatId = chatId.Value }),
            p => p.Items.Length > 0, attempts: 300);
        var links = await WaitFor(
            () => CallTool<McpContentPage<McpLinkItem>>(client, "list_links", new { chatId = chatId.Value }),
            p => p.Items.Length > 0, attempts: 300);

        // assert
        media.Items.Should().ContainSingle().Which.FileName.Should().Be("shot.png");
        files.Items.Should().ContainSingle().Which.FileName.Should().Be("notes.txt");
        links.Items.Should().ContainSingle().Which.Url.Should().Be("https://example.net/mcp");
        media.PageCount.Should().Be(1);
        media.NextPeriodKey.Should().BeNull();
    }

    [Fact]
    public async Task UploadedPictureShouldBeUsableAsChatPicture()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();
        var png = TestImages.Png(32, 32);

        // act
        var begun = await CallTool<McpUploadStatus>(client, "begin_upload", new {
            fileName = "icon.png", contentType = "image/png", length = png.Length, purpose = "chat_picture",
        });
        await CallTool<McpUploadStatus>(client, "append_upload", new {
            uploadId = begun.UploadId, offset = 0, dataBase64 = Convert.ToBase64String(png),
        });
        var finished = await CallTool<McpUploadStatus>(client, "finish_upload", new { uploadId = begun.UploadId });
        var chat = await CallTool<McpChatDetails>(client, "create_chat", new {
            title = "Pictured", isPublic = true, pictureMediaId = finished.Media!.MediaId,
        });

        // assert
        chat.PictureUrl.Should().NotBeNullOrEmpty();
    }
}
