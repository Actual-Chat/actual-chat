using ActualChat.Hashing;

namespace ActualChat.Chat.UnitTests;

public class ChatBackendCommandSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly UserId TestUserId = UserId.New();
    private static readonly PlaceId TestPlaceId = PlaceId.New();

    // AuthorsBackend

    [Fact]
    public void AuthorsBackend_Upsert_Basic()
    {
        var diff = new AuthorDiff {
            AvatarId = "avatar-1",
        };
        var cmd = new AuthorsBackend_Upsert(TestChatId, null, TestUserId, null, diff);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void AuthorsBackend_Remove_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new AuthorsBackend_Remove(null, authorId, null);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void AuthorsBackend_CopyChat_Basic()
    {
        var newChatId = GroupChatId.New();
        var cmd = new AuthorsBackend_CopyChat(TestChatId, newChatId, [], "corr-1");
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.OldChatId.Should().Be(original.OldChatId);
                deserialized.NewChatId.Should().Be(original.NewChatId);
                deserialized.CorrelationId.Should().Be(original.CorrelationId);
            }, Out);
    }

    // ChatsBackend

    [Fact]
    public void ChatsBackend_Change_Create()
    {
        var diff = new ChatDiff { Title = "New Chat" };
        var cmd = new ChatsBackend_Change(null, null, Change.Create(diff), TestUserId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatsBackend_Change_Update()
    {
        var diff = new ChatDiff { Title = "Updated Chat" };
        var cmd = new ChatsBackend_Change(TestChatId, 1, Change.Update(diff));
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatsBackend_ChangeEntry_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var diff = new ChatEntryDiff { Content = "Updated" };
        var cmd = new ChatsBackend_ChangeEntry(entryId, null, Change.Create(diff));
        cmd.PassThroughModernSerializers();
    }

    [Fact]
    public void ChatsBackend_RemoveOwnChats_Basic()
    {
        var cmd = new ChatsBackend_RemoveOwnChats(TestUserId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatsBackend_RemoveOwnEntries_Basic()
    {
        var cmd = new ChatsBackend_RemoveOwnEntries(TestUserId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatsBackend_CreateNotesChat_Basic()
    {
        var cmd = new ChatsBackend_CreateNotesChat(TestUserId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatBackend_CopyChat_Basic()
    {
        var cmd = new ChatBackend_CopyChat(TestChatId, TestPlaceId, "corr-1");
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatBackend_CopyChatResult_Basic()
    {
        var result = new ChatBackend_CopyChatResult(true, false, 42);
        result.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatsBackend_ChangeChatCopyState_Basic()
    {
        var diff = new ChatCopyStateDiff { IsCopiedSuccessfully = true };
        var cmd = new ChatsBackend_ChangeChatCopyState(TestChatId, null, Change.Create(diff));
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatsBackend_UpdateReadPositionsStat_Basic()
    {
        var cmd = new ChatsBackend_UpdateReadPositionsStat(TestChatId, TestUserId, 42);
        cmd.AssertPassesThroughSerializers();
    }

    // ChatsBackend_CreateAttachments
    [Fact]
    public void ChatsBackend_CreateAttachments_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var mediaId = MediaId.New("scope1");
        var attachment = new ChatEntryAttachment("att-1", 1) {
            EntryId = entryId,
            Index = 0,
            MediaId = mediaId,
        };
        var cmd = new ChatsBackend_CreateAttachments([attachment]);
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Attachments.Length.Should().Be(original.Attachments.Length);
                deserialized.Attachments[0].Id.Should().Be(original.Attachments[0].Id);
            }, Out);
    }

    // AliasBackend

    [Fact]
    public void AliasBackend_Change_Basic()
    {
        var aliasId = AliasId.Parse("test-alias");
        var alias = new Alias(aliasId, 0) { Kind = AliasKind.Chat, TargetId = TestChatId.Value };
        var cmd = new AliasBackend_Change(aliasId, null, Change.Create(alias));
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Id.Should().Be(original.Id);
            }, Out);
    }

    // ChatsUpgradeBackend

    [Fact]
    public void ChatsUpgradeBackend_CreateDefaultChat_Basic()
    {
        var cmd = new ChatsUpgradeBackend_CreateDefaultChat();
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatsUpgradeBackend_CreateAnnouncementsChat_Basic()
    {
        var cmd = new ChatsUpgradeBackend_CreateAnnouncementsChat();
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatsUpgradeBackend_CreateFeedbackTemplateChat_Basic()
    {
        var cmd = new ChatsUpgradeBackend_CreateFeedbackTemplateChat();
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChatsUpgradeBackend_UpgradeChat_Basic()
    {
        var cmd = new ChatsUpgradeBackend_UpgradeChat(TestChatId);
        cmd.AssertPassesThroughSerializers();
    }

    // ChatEntryLanguagesBackend

    [Fact]
    public void ChatEntryLanguagesBackend_Detect_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var cmd = new ChatEntryLanguagesBackend_Detect(entryId, new HashString("SHA256 Base16 abc123"));
        cmd.AssertPassesThroughSerializers();
    }

    // ConversationBackend

    [Fact]
    public void ConversationBackend_Change_Basic()
    {
        var convId = ConversationId.New(TestChatId, 0);
        var diff = new ConversationDiff { Title = "Updated" };
        var cmd = new ConversationBackend_Change(convId, null, Change.Create(diff));
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.ConversationId.Should().Be(original.ConversationId);
            }, Out);
    }

    [Fact]
    public void ConversationBackend_Summarize_Basic()
    {
        var cmd = new ConversationBackend_Summarize(TestChatId, [new Range<long>(0, 100)]);
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.ChatId.Should().Be(original.ChatId);
            }, Out);
    }

    [Fact]
    public void ConversationBackend_Materialize_Basic()
    {
        // arrange
        var convId = ConversationId.New(TestChatId, 0);
        var conversation = new Conversation(convId) { Title = "Voice chat", Summary = "Summary" };

        // act
        var cmd = new ConversationBackend_Materialize(conversation);

        // assert
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Conversation.Id.Should().Be(original.Conversation.Id);
                deserialized.Conversation.Title.Should().Be("Voice chat");
            }, Out);
    }

    // PlacesBackend

    [Fact]
    public void PlacesBackend_Change_Basic()
    {
        var diff = new PlaceDiff { Title = "New Place" };
        var cmd = new PlacesBackend_Change(null, null, Change.Create(diff), TestUserId);
        cmd.AssertPassesThroughSerializers();
    }

    // ReactionsBackend

    [Fact]
    public void ReactionsBackend_React_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var authorId = AuthorId.New(TestChatId, 5);
        var reaction = new Reaction {
            Id = "reaction-1",
            AuthorId = authorId,
            EntryId = entryId,
            Emoji = Emojis.ThumbsUp,
        };
        var cmd = new ReactionsBackend_React(reaction);
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.Reaction.Id.Should().Be(original.Reaction.Id);
            }, Out);
    }

    // RolesBackend

    [Fact]
    public void RolesBackend_Change_Basic()
    {
        var roleId = RoleId.New(TestChatId, 1);
        var diff = new RoleDiff { Name = "Admin" };
        var cmd = new RolesBackend_Change(TestChatId, roleId, null, Change.Create(diff));
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.ChatId.Should().Be(original.ChatId);
                deserialized.RoleId.Should().Be(original.RoleId);
            }, Out);
    }

    // TranslationsBackend

    [Fact]
    public void TranslationsBackend_Change_Basic()
    {
        var sourceId = TranslationSourceId.New(ChatEntryId.New(TestChatId, 1));
        var translationId = TranslationId.New(sourceId, Languages.Russian);
        var diff = new TranslationDiff { Content = "Translated" };
        var cmd = new TranslationsBackend_Change(translationId, null, Change.Create(diff));
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void TranslationsBackend_Translate_Basic()
    {
        var sourceId = TranslationSourceId.New(ChatEntryId.New(TestChatId, 1));
        var cmd = new TranslationsBackend_Translate(sourceId, Languages.Russian, false, false);
        cmd.AssertPassesThroughSerializers();
    }

    // Data types

    [Fact]
    public void TextEntry_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var entry = new ChatEntrySlim(1, "Hello", authorId, new Moment(DateTime.UtcNow), null, false, null, false);
        entry.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.LocalId.Should().Be(original.LocalId);
                deserialized.Content.Should().Be(original.Content);
                deserialized.AuthorId.Should().Be(original.AuthorId);
            }, Out);
    }

    [Fact]
    public void ReadPositionsStatBackend_Basic()
    {
        var stat = new ReadPositionsStatBackend(
            TestChatId,
            10,
            [new UserReadPosition(TestUserId, 42)]);
        stat.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.ChatId.Should().Be(original.ChatId);
                deserialized.StartTrackingEntryLid.Should().Be(original.StartTrackingEntryLid);
            }, Out);
    }

    // Query types

    [Fact]
    public void ChangedAuthorsQuery_Basic()
    {
        var query = new ChangedAuthorsQuery { MinVersion = 1, Limit = 100 };
        query.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChangedChatsQuery_Basic()
    {
        var query = new ChangedChatsQuery { LastId = TestChatId, Limit = 100, MinVersion = 1 };
        query.AssertPassesThroughSerializers();
    }

    [Fact]
    public void ChangedEntriesQuery_Basic()
    {
        var query = new ChangedEntriesQuery { ChatId = TestChatId, MinVersion = 1, Limit = 100 };
        query.AssertPassesThroughSerializers();
    }
}
