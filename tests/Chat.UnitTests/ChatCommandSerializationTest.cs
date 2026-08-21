
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
        var cmd = new Chats_GetOrCreateFromTemplate { Session = TestSession, TemplateChatId = TestChatId };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Chats_RemoveEntry_Basic()
    {
        var cmd = new Chats_RemoveEntry { Session = TestSession, ChatId = TestChatId, LocalId = 42 };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Chats_RestoreEntry_Basic()
    {
        var cmd = new Chats_RestoreEntry { Session = TestSession, ChatId = TestChatId, LocalId = 42 };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Chats_RemoveEntries_Basic()
    {
        var cmd = new Chats_RemoveEntries { Session = TestSession, ChatId = TestChatId, LocalIds = [1L, 2L, 3L] };
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ChatId.Should().Be(original.ChatId);
            }, Out);
    }

    [Fact]
    public void Chats_RestoreEntries_Basic()
    {
        var cmd = new Chats_RestoreEntries { Session = TestSession, ChatId = TestChatId, LocalIds = [1L, 2L, 3L] };
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ChatId.Should().Be(original.ChatId);
            }, Out);
    }

    [Fact]
    public void Chats_UpsertEntry_Basic()
    {
        var cmd = new Chats_UpsertEntry {
            Session = TestSession,
            ChatId = TestChatId,
            LocalId = null,
            Text = "Hello, world!",
        };
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ChatId.Should().Be(original.ChatId);
                deserialized.Text.Should().Be(original.Text);
            }, Out);
    }

    [Fact]
    public void Chats_UpsertEntry_WithReply()
    {
        var cmd = new Chats_UpsertEntry {
            Session = TestSession,
            ChatId = TestChatId,
            LocalId = 1,
            Text = "Reply text",
            RepliedEntryLid = Option.Some<long?>(5),
        };
        cmd.AssertPassesThroughSerializers(
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
        var cmd = new Chats_Change {
            Session = TestSession,
            ChatId = null,
            ExpectedVersion = null,
            Change = Change.Create(diff),
        };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Chats_Change_Update()
    {
        var diff = new ChatDiff { Title = "Updated Chat" };
        var cmd = new Chats_Change {
            Session = TestSession,
            ChatId = TestChatId,
            ExpectedVersion = 1,
            Change = Change.Update(diff),
        };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Chats_ForwardEntries_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var destChatId = GroupChatId.New();
        var cmd = new Chats_ForwardEntries {
            Session = TestSession,
            ChatId = TestChatId,
            ChatEntries = [entryId],
            DestinationChatIds = [destChatId],
        };
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ChatId.Should().Be(original.ChatId);
            }, Out);
    }

    [Fact]
    public void Chat_CopyChat_Basic()
    {
        var cmd = new Chat_CopyChat {
            Session = TestSession,
            SourceChatId = TestChatId,
            PlaceId = TestPlaceId,
            CorrelationId = "corr-1",
        };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Chat_PublishCopiedChat_Basic()
    {
        var newChatId = PlaceChatId.New(TestPlaceId);
        var cmd = new Chat_PublishCopiedChat {
            Session = TestSession,
            NewChatId = newChatId,
            SourceChatId = TestChatId,
        };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Chat_CopyChatResult_Basic()
    {
        var result = new Chat_CopyChatResult(true, false);
        result.AssertPassesThroughSerializers();
    }

    // Authors commands

    [Fact]
    public void Authors_Join_Basic()
    {
        var cmd = new Authors_Join { Session = TestSession, ChatId = TestChatId };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Authors_Leave_Basic()
    {
        var cmd = new Authors_Leave { Session = TestSession, ChatId = TestChatId };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Authors_Invite_Basic()
    {
        var cmd = new Authors_Invite { Session = TestSession, ChatId = TestChatId, UserIds = [TestUserId] };
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ChatId.Should().Be(original.ChatId);
            }, Out);
    }

    [Fact]
    public void Authors_Exclude_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Authors_Exclude { Session = TestSession, AuthorId = authorId };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Authors_Restore_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Authors_Restore { Session = TestSession, AuthorId = authorId };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Authors_SetAvatar_Basic()
    {
        var cmd = new Authors_SetAvatar { Session = TestSession, ChatId = TestChatId, AvatarId = "avatar-1" };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Authors_ChangeRole_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Authors_ChangeRole {
            Session = TestSession,
            AuthorId = authorId,
            SystemRole = SystemRole.Moderator,
            IsInRole = true,
        };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    [Obsolete("2026.08: Use Authors_ChangeRole. Old clients only.")]
    public void Authors_PromoteToOwner_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Authors_PromoteToOwner { Session = TestSession, AuthorId = authorId };
        cmd.AssertPassesThroughSerializers();
    }

    // Roles commands

    [Fact]
    public void Roles_Change_Basic()
    {
        var roleId = RoleId.New(TestChatId, 1);
        var diff = new RoleDiff { Name = "Moderator" };
        var cmd = new Roles_Change {
            Session = TestSession,
            ChatId = TestChatId,
            RoleId = roleId,
            ExpectedVersion = null,
            Change = Change.Create(diff),
        };
        cmd.AssertPassesThroughSerializers(
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
        var cmd = new Reactions_React { Session = TestSession, Reaction = reaction };
        cmd.AssertPassesThroughSerializers(
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
        var cmd = new Places_Change {
            Session = TestSession,
            PlaceId = null,
            ExpectedVersion = null,
            Change = Change.Create(diff),
        };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Places_Join_Basic()
    {
        var cmd = new Places_Join { Session = TestSession, PlaceId = TestPlaceId };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Places_Invite_Basic()
    {
        var cmd = new Places_Invite { Session = TestSession, PlaceId = TestPlaceId, UserIds = [TestUserId] };
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.PlaceId.Should().Be(original.PlaceId);
            }, Out);
    }

    [Fact]
    public void Places_Exclude_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Places_Exclude { Session = TestSession, AuthorId = authorId };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Places_Restore_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Places_Restore { Session = TestSession, AuthorId = authorId };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Places_ChangeRole_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Places_ChangeRole {
            Session = TestSession,
            AuthorId = authorId,
            SystemRole = SystemRole.Moderator,
            IsInRole = false,
        };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    [Obsolete("2026.08: Use Places_ChangeRole. Old clients only.")]
    public void Places_PromoteToOwner_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new Places_PromoteToOwner { Session = TestSession, AuthorId = authorId };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Places_Leave_Basic()
    {
        var cmd = new Places_Leave { Session = TestSession, PlaceId = TestPlaceId };
        cmd.AssertPassesThroughSerializers();
    }

    // Conversations commands

    [Fact]
    public void Conversations_Summarize_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var convId = ConversationId.New(chatId, 0);
        var cmd = new Conversations_Summarize { Session = TestSession, ConversationId = convId };
        cmd.AssertPassesThroughSerializers();
    }

    // ChatThreads commands

    [Fact]
    public void ChatThreads_Start_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var cmd = new ChatThreads_Start {
            Session = TestSession,
            ParentChatId = TestChatId,
            Title = "Thread Title",
            Description = "Description",
            EntryIds = [entryId],
        };
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Session.Should().Be(original.Session);
                deserialized.ParentChatId.Should().Be(original.ParentChatId);
                deserialized.Title.Should().Be(original.Title);
            }, Out);
    }
}
