using ActualChat.Chat;

namespace ActualChat.Chat.UnitTests;

public class ChatCommandSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Session TestSession = Session.New();
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly PlaceId TestPlaceId = PlaceId.New();
    private static readonly UserId TestUserId = UserId.New();

    [Fact]
    public void Chats_GetOrCreateFromTemplate_Basic()
    {
        var cmd = new Chats_GetOrCreateFromTemplate(TestSession, TestChatId);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Chats_RemoveEntry_Basic()
    {
        var cmd = new Chats_RemoveEntry(TestSession, TestChatId, 42);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Chats_RestoreEntry_Basic()
    {
        var cmd = new Chats_RestoreEntry(TestSession, TestChatId, 42);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Chats_RemoveEntries_Basic()
    {
        var cmd = new Chats_RemoveEntries(TestSession, TestChatId, [1L, 2L, 3L]);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ChatId.Should().Be(original.ChatId);
            }, Out);
    }

    [Fact]
    public void Chats_RestoreEntries_Basic()
    {
        var cmd = new Chats_RestoreEntries(TestSession, TestChatId, [1L, 2L, 3L]);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ChatId.Should().Be(original.ChatId);
            }, Out);
    }

    [Fact]
    public void Chats_UpsertEntry_Basic()
    {
        var cmd = new Chats_UpsertEntry(TestSession, TestChatId, null) { Text = "Hello, world!" };
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ChatId.Should().Be(original.ChatId);
                deserialized.Text.Should().Be(original.Text);
            }, Out);
    }

    [Fact]
    public void Chats_UpsertEntry_WithReply()
    {
        var cmd = new Chats_UpsertEntry(TestSession, TestChatId, 1) { Text = "Reply text", RepliedEntryLid = Option.Some<long?>(5) };
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ChatId.Should().Be(original.ChatId);
                deserialized.Text.Should().Be(original.Text);
            }, Out);
    }

    [Fact]
    public void Chats_Change_Create()
    {
        var diff = new ChatDiff { Title = "New Chat", IsPublic = true };
        var cmd = new Chats_Change(TestSession, null, null, Change.Create(diff));
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Chats_Change_Update()
    {
        var diff = new ChatDiff { Title = "Updated Chat" };
        var cmd = new Chats_Change(TestSession, TestChatId, 1, Change.Update(diff));
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Chats_ForwardEntries_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var destChatId = GroupChatId.New();
        var cmd = new Chats_ForwardEntries(TestSession, TestChatId, [entryId], [destChatId]);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ChatId.Should().Be(original.ChatId);
            }, Out);
    }

    [Fact]
    public void Chat_CopyChat_Basic()
    {
        var cmd = new Chat_CopyChat(TestSession, TestChatId, TestPlaceId, "corr-1");
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Chat_PublishCopiedChat_Basic()
    {
        var newChatId = PlaceChatId.New(TestPlaceId);
        var cmd = new Chat_PublishCopiedChat(TestSession, newChatId, TestChatId);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Chat_CopyChatResult_Basic()
    {
        var result = new Chat_CopyChatResult(true, false);
        result.AssertPassesThroughAllSerializers();
    }

    // Authors commands

    [Fact]
    public void Authors_Join_Basic()
    {
        var cmd = new Authors_Join(TestSession, TestChatId);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Authors_Leave_Basic()
    {
        var cmd = new Authors_Leave(TestSession, TestChatId);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Authors_Invite_Basic()
    {
        var cmd = new Authors_Invite(TestSession, TestChatId, [TestUserId]);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ChatId.Should().Be(original.ChatId);
            }, Out);
    }

    [Fact]
    public void Authors_Exclude_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Authors_Exclude(TestSession, authorId);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Authors_Restore_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Authors_Restore(TestSession, authorId);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Authors_SetAvatar_Basic()
    {
        var cmd = new Authors_SetAvatar(TestSession, TestChatId, "avatar-1");
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Authors_PromoteToOwner_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Authors_PromoteToOwner(TestSession, authorId);
        cmd.AssertPassesThroughAllSerializers();
    }

    // Roles commands

    [Fact]
    public void Roles_Change_Basic()
    {
        var roleId = RoleId.New(TestChatId, 1);
        var diff = new RoleDiff { Name = "Moderator" };
        var cmd = new Roles_Change(TestSession, TestChatId, roleId, null, Change.Create(diff));
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ChatId.Should().Be(original.ChatId);
            }, Out);
    }

    // Reactions commands

    [Fact]
    public void Reactions_React_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var authorId = AuthorId.New(TestChatId, 5);
        var reaction = new Reaction {
            Id = "reaction-1",
            AuthorId = authorId,
            EntryId = entryId,
            Emoji = Emojis.ThumbsUp,
        };
        var cmd = new Reactions_React(TestSession, reaction);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.Reaction.Id.Should().Be(original.Reaction.Id);
                deserialized.Reaction.Emoji.Should().Be(original.Reaction.Emoji);
            }, Out);
    }

    // Places commands

    [Fact]
    public void Places_Change_Create()
    {
        var diff = new PlaceDiff { Title = "New Place", IsPublic = true };
        var cmd = new Places_Change(TestSession, null, null, Change.Create(diff));
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Places_Join_Basic()
    {
        var cmd = new Places_Join(TestSession, TestPlaceId);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Places_Invite_Basic()
    {
        var cmd = new Places_Invite(TestSession, TestPlaceId, [TestUserId]);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.PlaceId.Should().Be(original.PlaceId);
            }, Out);
    }

    [Fact]
    public void Places_Exclude_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Places_Exclude(TestSession, authorId);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Places_Restore_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Places_Restore(TestSession, authorId);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Places_PromoteToOwner_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Places_PromoteToOwner(TestSession, authorId);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Places_Leave_Basic()
    {
        var cmd = new Places_Leave(TestSession, TestPlaceId);
        cmd.AssertPassesThroughAllSerializers();
    }

    // Conversations commands

    [Fact]
    public void Conversations_Summarize_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var convId = ConversationId.New(chatId, 0);
        var cmd = new Conversations_Summarize(TestSession, convId);
        cmd.AssertPassesThroughAllSerializers();
    }

    // ChatThreads commands

    [Fact]
    public void ChatThreads_Start_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var cmd = new ChatThreads_Start(TestSession, TestChatId, "Thread Title", "Description", [entryId]);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ParentChatId.Should().Be(original.ParentChatId);
                deserialized.Title.Should().Be(original.Title);
            }, Out);
    }
}
