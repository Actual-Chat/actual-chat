using ActualChat.Chat.Module;
using ActualChat.Testing.Host;
using ModelContextProtocol.Client;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class McpImportToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    private ChatSettings ChatSettings => Tester.AppServices.GetRequiredService<ChatSettings>();
    private IChatsBackend ChatsBackend => Tester.AppServices.GetRequiredService<IChatsBackend>();

    [Fact]
    public async Task ImportShouldRunEndToEndThroughTools()
    {
        // arrange
        var (alice, aliceMcp, bob, bobMcp, chatId) = await CreateChatWithMember();

        // act
        var session = await Start(aliceMcp, chatId, "start-1");
        var retried = await Start(aliceMcp, chatId, "start-1");
        var before = await CallTool<McpImportStatus>(aliceMcp, "get_import", new { chatId = chatId.Value });
        await Consent(aliceMcp, chatId, session.ImportId);
        await Consent(bobMcp, chatId, session.ImportId);
        var png = TestImages.Png(32, 24);
        var begun = await CallTool<McpUploadStatus>(aliceMcp, "begin_import_upload", new {
            chatId = chatId.Value, importId = session.ImportId, authorUserId = bob.Id.Value,
            fileName = "photo.png", contentType = "image/png", length = png.Length,
        });
        var appended = await CallTool<McpUploadStatus>(aliceMcp, "append_upload", new {
            uploadId = begun.UploadId, offset = 0, dataBase64 = Convert.ToBase64String(png),
        });
        var finished = await CallTool<McpUploadStatus>(aliceMcp, "finish_import_upload", new {
            chatId = chatId.Value, importId = session.ImportId, uploadId = begun.UploadId,
        });
        var batch = new {
            chatId = chatId.Value,
            importId = session.ImportId,
            batchId = "batch-1",
            messages = new object[] {
                new { authorUserId = alice.Id.Value, sentAt = SentAt(session, 1), text = "imported by alice" },
                new {
                    authorUserId = bob.Id.Value, sentAt = SentAt(session, 2), text = "imported by bob",
                    uploadIds = new[] { begun.UploadId },
                },
            },
        };
        var imported = await CallTool<McpImportMessagesResult>(aliceMcp, "import_messages", batch);
        var resent = await CallTool<McpImportMessagesResult>(aliceMcp, "import_messages", batch);
        var reply = await CallTool<McpImportMessagesResult>(aliceMcp, "import_messages", new {
            chatId = chatId.Value,
            importId = session.ImportId,
            batchId = "batch-2",
            messages = new[] {
                new {
                    authorUserId = bob.Id.Value, sentAt = SentAt(session, 3), text = "a reply",
                    replyToId = imported.Items[0].Id,
                },
            },
        });
        var after = await CallTool<McpImportStatus>(aliceMcp, "get_import", new { chatId = chatId.Value });
        var ended = await CallTool<McpImportEndResult>(aliceMcp, "end_import", new {
            chatId = chatId.Value, importId = session.ImportId,
        });
        var messages = await CallTool<McpListMessagesResult>(aliceMcp, "list_messages", new { chatId = chatId.Value });

        // assert
        retried.ImportId.Should().Be(session.ImportId, "a retry with the same requestId is the same start");
        session.Scope.Should().Be("chat");
        before.Active.Should().BeTrue();
        before.ConsentingCount.Should().Be(0);
        before.Pending!.Select(x => x.UserId).Should().BeEquivalentTo([alice.Id.Value, bob.Id.Value]);
        before.Pending!.Single(x => x.UserId == bob.Id.Value).PeerChatId
            .Should().Be(PeerChatId.New(alice.Id, bob.Id).Value);
        before.Pending!.Single(x => x.UserId == alice.Id.Value).PeerChatId.Should().BeNull();
        appended.Offset.Should().Be(png.Length);
        appended.MediaId.Should().BeNull("an import upload has no media until it is finished");
        finished.Stage.Should().Be("Ready");
        finished.Media!.Width.Should().Be(32);
        imported.Items.Should().OnlyContain(x => x.Error == null && x.Id != null);
        resent.Items.Select(x => x.Id).Should().Equal(imported.Items.Select(x => x.Id));
        after.MyConsent.Should().BeTrue();
        after.ConsentPercent.Should().Be(100);
        after.Pending.Should().BeEmpty();
        ended.NonConsentingCount.Should().Be(0);
        var bobMessage = messages.Messages.Single(m => m.Id == imported.Items[1].Id);
        bobMessage.Text.Should().Be("imported by bob");
        bobMessage.CreatedAt.Should().Be(SentAt(session, 2), "createdAt reads back the imported sentAt");
        bobMessage.Attachments.Should().ContainSingle(a => a.MediaId == finished.Media.MediaId);
        messages.Messages.Single(m => m.Id == reply.Items[0].Id).RepliedToId.Should().Be(imported.Items[0].Id);
        (await CallTool<McpImportStatus>(aliceMcp, "get_import", new { chatId = chatId.Value }))
            .Active.Should().BeFalse();
    }

    [Fact]
    public async Task StartImportWithoutConfirmShouldDoNothing()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false);
        var client = await CreateClient();

        // act
        var error = await CallToolExpectingError(client, "start_import", new { chatId = chatId.Value });
        var status = await CallTool<McpImportStatus>(client, "get_import", new { chatId = chatId.Value });

        // assert
        error.Should().Contain("confirm: true");
        status.Active.Should().BeFalse();
        await CallTool<long>(client, "post_message", new { chatId = chatId.Value, text = "still open" });
    }

    [Fact]
    public async Task StartImportShouldFailWhenAMessageLandsAfterItsStart()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false);
        var client = await CreateClient();
        await WaitForJoinEntries(chatId, 1);
        // Stands in for a message posted through a node that hadn't seen the import yet
        var author = await Tester.Authors.GetOwn(Tester.Session, chatId, CancellationToken.None);
        var createEntryCmd = new ChatsBackend_ChangeEntry(ChatEntryId.New(chatId, 0), null,
            Change.Create(new ChatEntryDiff {
                AuthorId = author!.Id, Content = "late", BeginsAt = Moment.Now + TimeSpan.FromMinutes(1),
            }));
        var late = await Tester.Commander.Call(createEntryCmd);
        ChatSettings.ImportStartSettleDelay = TimeSpan.FromMilliseconds(100);

        // act
        var error = await CallToolExpectingError(client, "start_import", new { chatId = chatId.Value, confirm = true });

        // assert
        error.Should().Contain($"Message #{late.LocalId} was posted while the import was starting");
        (await CallTool<McpImportStatus>(client, "get_import", new { chatId = chatId.Value }))
            .Active.Should().BeFalse();
    }

    [Fact]
    public async Task NonOwnerShouldSeeCountsButNotManageTheImport()
    {
        // arrange
        var (_, aliceMcp, bob, bobMcp, chatId) = await CreateChatWithMember();

        // act
        var startError = await CallToolExpectingError(bobMcp, "start_import",
            new { chatId = chatId.Value, confirm = true });
        var session = await Start(aliceMcp, chatId);
        var status = await CallTool<McpImportStatus>(bobMcp, "get_import", new { chatId = chatId.Value });
        var importError = await CallToolExpectingError(bobMcp, "import_messages", new {
            chatId = chatId.Value, importId = session.ImportId, batchId = "b",
            messages = new[] { new { authorUserId = bob.Id.Value, sentAt = SentAt(session, 1), text = "x" } },
        });
        var uploadError = await CallToolExpectingError(bobMcp, "begin_import_upload", new {
            chatId = chatId.Value, importId = session.ImportId, authorUserId = bob.Id.Value,
            fileName = "a.txt", contentType = "text/plain", length = 1,
        });
        var endError = await CallToolExpectingError(bobMcp, "end_import",
            new { chatId = chatId.Value, importId = session.ImportId });
        await Consent(bobMcp, chatId, session.ImportId);
        var consented = await CallTool<McpImportStatus>(bobMcp, "get_import", new { chatId = chatId.Value });
        await End(aliceMcp, chatId, session);

        // assert
        startError.Should().Contain("owners");
        importError.Should().Contain("owners");
        uploadError.Should().Contain("owners");
        endError.Should().Contain("owners");
        status.Active.Should().BeTrue();
        status.NonConsentingCount.Should().Be(2);
        status.Pending.Should().BeNull("only owners see who has not consented");
        status.MyConsent.Should().BeFalse();
        consented.MyConsent.Should().BeTrue();
        consented.ConsentingCount.Should().Be(1);
    }

    [Fact]
    public async Task PlaceImportShouldBeReportedForEveryPlaceChat()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var place = await Tester.CreatePlace(true);
        var (placeChatId, _) = await Tester.CreateChat(true, placeId: place.Id);
        var client = await CreateClient();

        // act
        var session = await Start(client, place.Id.RootChatId);
        var status = await CallTool<McpImportStatus>(client, "get_import", new { chatId = placeChatId.Value });
        var postError = await CallToolExpectingError(client, "post_message",
            new { chatId = placeChatId.Value, text = "blocked" });
        await End(client, place.Id.RootChatId, session);

        // assert
        session.Scope.Should().Be("place");
        status.Active.Should().BeTrue();
        status.ImportId.Should().Be(session.ImportId);
        status.Scope.Should().Be("place");
        status.PlaceId.Should().Be(place.Id.Value);
        postError.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ImportShouldBlockOnlyTheImportedChat()
    {
        // arrange
        var (alice, aliceMcp, bob, _, chatId) = await CreateChatWithMember();
        var peerChatId = PeerChatId.New(alice.Id, bob.Id);
        var session = await Start(aliceMcp, chatId);

        // act
        var blockedError = await CallToolExpectingError(aliceMcp, "post_message",
            new { chatId = chatId.Value, text = "blocked" });
        var reminderId = await CallTool<long>(aliceMcp, "post_message",
            new { chatId = peerChatId.Value, text = "Please allow the import" });
        await End(aliceMcp, chatId, session);
        var afterId = await CallTool<long>(aliceMcp, "post_message",
            new { chatId = chatId.Value, text = "open again" });

        // assert
        blockedError.Should().NotBeEmpty();
        reminderId.Should().BePositive();
        afterId.Should().BePositive();
    }

    [Fact]
    public async Task RejectedMessagesShouldBeReportedPerItem()
    {
        // arrange
        var (alice, aliceMcp, bob, _, chatId) = await CreateChatWithMember();
        var session = await Start(aliceMcp, chatId);
        await Consent(aliceMcp, chatId, session.ImportId);
        var future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();

        // act
        var result = await CallTool<McpImportMessagesResult>(aliceMcp, "import_messages", new {
            chatId = chatId.Value,
            importId = session.ImportId,
            batchId = "mixed",
            messages = new object[] {
                new { authorUserId = alice.Id.Value, sentAt = SentAt(session, 2), text = "accepted" },
                new { authorUserId = bob.Id.Value, sentAt = SentAt(session, 3), text = "no consent" },
                new { authorUserId = alice.Id.Value, sentAt = future, text = "future" },
                new {
                    authorUserId = alice.Id.Value, sentAt = SentAt(session, 4), text = "missing upload",
                    uploadIds = new[] { UploadId.New().Value },
                },
            },
        });
        var older = await CallTool<McpImportMessagesResult>(aliceMcp, "import_messages", new {
            chatId = chatId.Value,
            importId = session.ImportId,
            batchId = "older",
            messages = new[] { new { authorUserId = alice.Id.Value, sentAt = SentAt(session, 1), text = "older" } },
        });
        var batchIdError = await CallToolExpectingError(aliceMcp, "import_messages", new {
            chatId = chatId.Value,
            importId = session.ImportId,
            batchId = "",
            messages = new[] { new { authorUserId = alice.Id.Value, sentAt = SentAt(session, 6), text = "x" } },
        });
        var changedError = await CallToolExpectingError(aliceMcp, "import_messages", new {
            chatId = chatId.Value,
            importId = session.ImportId,
            batchId = "mixed",
            messages = new[] { new { authorUserId = alice.Id.Value, sentAt = SentAt(session, 5), text = "changed" } },
        });
        await End(aliceMcp, chatId, session);

        // assert
        result.Items.Should().HaveCount(4);
        result.Items[0].Id.Should().NotBeNull();
        result.Items[0].Error.Should().BeNull();
        result.Items[1].Error.Should().Contain("consented");
        result.Items[2].Error.Should().Contain("past");
        result.Items[3].Error.Should().NotBeNull();
        older.Items[0].Error.Should().Contain("later than the last accepted entry");
        changedError.Should().Contain("different entries");
        batchIdError.Should().Contain("batchId");
    }

    [Fact]
    public async Task AbortUploadShouldDiscardAnImportUpload()
    {
        // arrange
        var (alice, aliceMcp, _, _, chatId) = await CreateChatWithMember();
        var session = await Start(aliceMcp, chatId);
        await Consent(aliceMcp, chatId, session.ImportId);
        var begun = await CallTool<McpUploadStatus>(aliceMcp, "begin_import_upload", new {
            chatId = chatId.Value, importId = session.ImportId, authorUserId = alice.Id.Value,
            fileName = "a.txt", contentType = "text/plain", length = 4,
        });

        // act
        await CallTool(aliceMcp, "abort_upload", new { uploadId = begun.UploadId });
        var appendError = await CallToolExpectingError(aliceMcp, "append_upload", new {
            uploadId = begun.UploadId, offset = 0, dataBase64 = Convert.ToBase64String("data"u8.ToArray()),
        });
        await End(aliceMcp, chatId, session);

        // assert
        appendError.Should().Contain("Upload not found", "the reason must reach the caller");
    }

    [Fact]
    public async Task ImportUploadShouldBeUsableOnlyThroughImportTools()
    {
        // arrange
        var (alice, aliceMcp, _, bobMcp, chatId) = await CreateChatWithMember();
        var png = TestImages.Png(40, 20);
        var source = await UploadFromChunks(aliceMcp, chatId, png);
        var sourceUrl = source.Media!.Url.Replace("127.0.0.1", "localhost");
        var session = await Start(aliceMcp, chatId);
        await Consent(aliceMcp, chatId, session.ImportId);
        var begun = await CallTool<McpUploadStatus>(aliceMcp, "begin_import_upload", new {
            chatId = chatId.Value, importId = session.ImportId, authorUserId = alice.Id.Value,
            fileName = "a.txt", contentType = "text/plain", length = 4,
        });
        var data = Convert.ToBase64String("data"u8.ToArray());

        // act
        var copied = await CallTool<McpUploadStatus>(aliceMcp, "import_upload_from_url", new {
            url = sourceUrl, chatId = chatId.Value, importId = session.ImportId, authorUserId = alice.Id.Value,
        });
        var bobAppendError = await CallToolExpectingError(bobMcp, "append_upload",
            new { uploadId = begun.UploadId, offset = 0, dataBase64 = data });
        await CallTool<McpUploadStatus>(aliceMcp, "append_upload",
            new { uploadId = begun.UploadId, offset = 0, dataBase64 = data });
        var finishError = await CallToolExpectingError(aliceMcp, "finish_upload", new { uploadId = begun.UploadId });
        await End(aliceMcp, chatId, session);

        // assert
        copied.Stage.Should().Be("Ready");
        copied.Media!.Width.Should().Be(40);
        bobAppendError.Should().NotBeEmpty("an import upload belongs to the owner who began it");
        finishError.Should().Contain("finish_import_upload");
    }

    [Fact]
    public async Task SinglePlaceChatImportShouldBlockOnlyThatChat()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var place = await Tester.CreatePlace(true);
        var (importedChatId, _) = await Tester.CreateChat(true, placeId: place.Id);
        var (otherChatId, _) = await Tester.CreateChat(true, placeId: place.Id);
        var client = await CreateClient();

        // act
        var session = await Start(client, importedChatId);
        var status = await CallTool<McpImportStatus>(client, "get_import", new { chatId = importedChatId.Value });
        var other = await CallTool<McpImportStatus>(client, "get_import", new { chatId = otherChatId.Value });
        var postedId = await CallTool<long>(client, "post_message", new { chatId = otherChatId.Value, text = "open" });
        await End(client, importedChatId, session);

        // assert
        session.Scope.Should().Be("chat");
        status.Scope.Should().Be("chat");
        status.PlaceId.Should().BeNull();
        other.Active.Should().BeFalse();
        postedId.Should().BePositive();
    }

    [Fact]
    public async Task EndImportShouldReportMembersWhoDidNotConsent()
    {
        // arrange
        var (_, aliceMcp, _, _, chatId) = await CreateChatWithMember();
        var session = await Start(aliceMcp, chatId);
        await Consent(aliceMcp, chatId, session.ImportId);

        // act
        var ended = await CallTool<McpImportEndResult>(aliceMcp, "end_import", new {
            chatId = chatId.Value, importId = session.ImportId,
        });

        // assert
        ended.NonConsentingCount.Should().Be(1, "bob never consented");
    }

    // Private methods

    private async Task<(AccountFull Alice, McpClient AliceMcp, AccountFull Bob, McpClient BobMcp, ChatId ChatId)>
        CreateChatWithMember()
    {
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        var (chatId, inviteId) = await Tester.CreateChat(isPublicChat: true);
        var bob = await Tester.SignInAsUniqueBob();
        var bobKey = await IssueApiKey("bob");
        await Tester.JoinChat(chatId, inviteId);
        await Tester.SignIn(alice);
        await WaitForJoinEntries(chatId, 2);
        var aliceMcp = await CreateClientWithRawKey(aliceKey);
        var bobMcp = await CreateClientWithRawKey(bobKey);
        return (alice, aliceMcp, bob, bobMcp, chatId);
    }

    private async Task WaitForJoinEntries(ChatId chatId, int memberCount)
        // Each member's joined entry lands asynchronously, and one landing after the start fails it
        => await TestWait.When(async ct => {
            (await ChatsBackend.GetMaxLid(chatId, true, ct)).Should().BeGreaterThanOrEqualTo(memberCount);
        });

    private async Task<McpImportSession> Start(McpClient client, ChatId chatId, string? requestId = null)
    {
        // None of these tests posts while an import is starting, so a short settle delay is enough
        ChatSettings.ImportStartSettleDelay = TimeSpan.FromMilliseconds(100);
        return await CallTool<McpImportSession>(client, "start_import",
            new { chatId = chatId.Value, confirm = true, requestId });
    }

    private static Task Consent(McpClient client, ChatId chatId, string importId)
        => CallTool(client, "consent_to_import", new { chatId = chatId.Value, importId });

    private static Task End(McpClient client, ChatId chatId, McpImportSession session)
        => CallTool<McpImportEndResult>(client, "end_import",
            new { chatId = chatId.Value, importId = session.ImportId });

    private static async Task<McpUploadStatus> UploadFromChunks(McpClient client, ChatId chatId, byte[] data)
    {
        var begun = await CallTool<McpUploadStatus>(client, "begin_upload", new {
            fileName = "source.png", contentType = "image/png", length = data.Length, chatId = chatId.Value,
        });
        await CallTool<McpUploadStatus>(client, "append_upload",
            new { uploadId = begun.UploadId, offset = 0, dataBase64 = Convert.ToBase64String(data) });
        return await CallTool<McpUploadStatus>(client, "finish_upload", new { uploadId = begun.UploadId });
    }

    private static long SentAt(McpImportSession session, int offsetMs)
        // Later than everything posted before the start, and still in the past
        => session.StartedAt + offsetMs;
}
