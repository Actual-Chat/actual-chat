using ActualChat.Contacts;
using ActualChat.Hashing;
using ActualChat.Media;
using ActualChat.Search;
using ChatModel = ActualChat.Chat.Chat;

namespace ActualChat.Chat.UnitTests;

public class ChatModelSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void Chat_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var chat = new ChatModel(chatId, 42) {
            Title = "Test Chat",
            IsPublic = true,
            CreatedAt = new Moment(DateTime.UtcNow),
            AllowGuestAuthors = true,
            AllowAnonymousAuthors = false,
            Description = "A test chat",
            SystemTag = "system",
        };

        var s = chat.PassThroughAllSerializers(Out);
        s.Id.Should().Be(chat.Id);
        s.Version.Should().Be(chat.Version);
        s.Title.Should().Be(chat.Title);
        s.IsPublic.Should().Be(chat.IsPublic);
        s.CreatedAt.Should().Be(chat.CreatedAt);
        s.AllowGuestAuthors.Should().Be(chat.AllowGuestAuthors);
        s.AllowAnonymousAuthors.Should().Be(chat.AllowAnonymousAuthors);
        s.Description.Should().Be(chat.Description);
        s.SystemTag.Should().Be(chat.SystemTag);
    }

    [Fact]
    public void ChatDiff_Basic()
    {
        var diff = new ChatDiff {
            Title = "Updated Title",
            IsPublic = true,
            AllowGuestAuthors = false,
            Description = "Updated description",
        };
        diff.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void ChatEntry_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 1);
        var entry = new TextEntry(entryId, 1) {
            AuthorId = AuthorId.New(chatId, 10),
            BeginsAt = new Moment(DateTime.UtcNow),
            Content = "Hello, world!",
            HasReactions = true,
            IsRemoved = false,
            ContentHash = HashString.None,
        };

        ChatEntry s = entry.PassThroughModernSerializers(Out);
        s.Id.Should().Be(entry.Id);
        s.Version.Should().Be(entry.Version);
        s.AuthorId.Should().Be(entry.AuthorId);
        s.BeginsAt.Should().Be(entry.BeginsAt);
        s.Content.Should().Be(entry.Content);
        s.HasReactions.Should().Be(entry.HasReactions);
        s.IsRemoved.Should().Be(entry.IsRemoved);
        s.ContentHash.Should().Be(entry.ContentHash);
    }

    [Fact]
    public void ChatEntry_WithNullableMoments()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 2);
        var now = new Moment(DateTime.UtcNow);
        var entry = new TextEntry(entryId, 1) {
            AuthorId = AuthorId.New(chatId, 10),
            BeginsAt = now,
            EndsAt = now + TimeSpan.FromMinutes(5),
            Content = "Test",
        };

        var s = entry.PassThroughModernSerializers(Out);
        s.EndsAt.Should().Be(entry.EndsAt);
    }

    [Fact]
    public void ChatEntry_WithNullMoments()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 3);
        var entry = new TextEntry(entryId, 1) {
            AuthorId = AuthorId.New(chatId, 10),
            BeginsAt = new Moment(DateTime.UtcNow),
            EndsAt = null,
            Content = "Test",
        };

        var s = entry.PassThroughModernSerializers(Out);
        s.EndsAt.Should().BeNull();
    }

    [Fact]
    public void ChatEntryDiff_Basic()
    {
        var diff = new ChatEntryDiff {
            Content = "Updated content",
            IsRemoved = true,
        };
        diff.PassThroughModernSerializers();
    }

    [Fact]
    public void Author_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var authorId = AuthorId.New(chatId, 5);
        var author = new Author(authorId, 1) {
            AvatarId = "avatar-1",
            IsAnonymous = false,
            HasLeft = false,
            Avatar = new Avatar("avatar-1") { Name = "TestUser" },
        };

        var s = author.PassThroughAllSerializers(Out);
        s.Id.Should().Be(author.Id);
        s.Version.Should().Be(author.Version);
        s.AvatarId.Should().Be(author.AvatarId);
        s.IsAnonymous.Should().Be(author.IsAnonymous);
        s.HasLeft.Should().Be(author.HasLeft);
        s.Avatar.Name.Should().Be(author.Avatar.Name);
    }

    [Fact]
    public void AuthorFull_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var authorId = AuthorId.New(chatId, 5);
        var userId = UserId.New();
        var author = new AuthorFull(userId, authorId, 1) {
            AvatarId = "avatar-1",
            IsAnonymous = false,
            HasLeft = false,
            Avatar = new Avatar("avatar-1") { Name = "TestUser" },
            RoleIds = [RoleId.New(chatId, 1)],
            CreatedAt = new Moment(DateTime.UtcNow),
        };

        var s = author.PassThroughAllSerializers(Out);
        s.Id.Should().Be(author.Id);
        s.UserId.Should().Be(author.UserId);
        s.RoleIds.Should().BeEquivalentTo(author.RoleIds);
        s.CreatedAt.Should().Be(author.CreatedAt);
    }

    [Fact]
    public void AuthorRules_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var rules = new AuthorRules(chatId, null, null, ChatPermissions.Write);

        var s = rules.PassThroughAllSerializers(Out);
        s.ChatId.Should().Be(rules.ChatId);
        s.Permissions.Should().Be(rules.Permissions);
        s.Author.Should().BeNull();
        s.Account.Should().BeNull();
    }

    [Fact]
    public void ChatTile_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 1);
        var entries = new[] {
            new TextEntry(entryId, 1) {
                AuthorId = AuthorId.New(chatId, 10),
                BeginsAt = new Moment(DateTime.UtcNow),
                Content = "Hello",
            },
        };
        var tile = new ChatTile(new Range<long>(0, 100), false, entries);

        var s = tile.PassThroughModernSerializers(Out);
        s.LidTileRange.Should().Be(tile.LidTileRange);
        s.IncludesRemoved.Should().Be(tile.IncludesRemoved);
        s.Entries.Length.Should().Be(tile.Entries.Length);
    }

    [Fact]
    public void ChatNews_Basic()
    {
        var news = new ChatNews(new Range<long>(1, 100));
        news.PassThroughModernSerializers();
    }

    [Fact]
    public void ChatRangeMeta_Basic()
    {
        var meta = new ChatRangeMeta(
            new Range<long>(0, 100),
            [new Range<long>(0, 50), new Range<long>(50, 100)],
            [new Range<long>(0, 25)],
            10,
            null,
            100);

        var s = meta.PassThroughAllSerializers(Out);
        s.LidRange.Should().Be(meta.LidRange);
        s.EntryLidRanges.Should().BeEquivalentTo(meta.EntryLidRanges);
        s.ConversationLidRanges.Should().BeEquivalentTo(meta.ConversationLidRanges);
        s.MinCount.Should().Be(meta.MinCount);
        s.PreviousLidTileStart.Should().Be(meta.PreviousLidTileStart);
        s.NextLidTileStart.Should().Be(meta.NextLidTileStart);
    }

    [Fact]
    public void ChatEntryRangeMeta_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var meta = new ChatEntryRangeMeta(
            chatId,
            [new Range<long>(0, 50)],
            null,
            50);

        var s = meta.PassThroughAllSerializers(Out);
        s.ChatId.Should().Be(meta.ChatId);
        s.EntryLidRange.Should().BeEquivalentTo(meta.EntryLidRange);
        s.PreviousEntryLid.Should().Be(meta.PreviousEntryLid);
        s.NextEntryLid.Should().Be(meta.NextEntryLid);
    }

    [Fact]
    public void ChatEntryLanguage_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 1);
        var lang = new ChatEntryLanguage(entryId, 1) {
            Languages = [Languages.English, Languages.Russian],
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
        };

        lang.AssertPassesThroughAllSerializers(v => {
            v.Id.Should().Be(lang.Id);
            v.Languages.Should().BeEquivalentTo(lang.Languages);
            v.CreatedAt.Should().Be(lang.CreatedAt);
            v.ModifiedAt.Should().Be(lang.ModifiedAt);
        }, Out);
    }

    [Fact]
    public void ChatLanguageTile_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 1);
        var tile = new ChatLanguageTile(
            new Range<long>(0, 100),
            [new ChatEntryLanguage(entryId, 1) { Languages = [Languages.English] }]);

        tile.AssertPassesThroughAllSerializers(v => {
            v.LidTileRange.Should().Be(tile.LidTileRange);
            v.Entries.Length.Should().Be(tile.Entries.Length);
            v.Entries[0].Id.Should().Be(tile.Entries[0].Id);
        }, Out);
    }

    [Fact]
    public void ChatCopyState_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        ChatId sourceId = GroupChatId.New();
        var state = new ChatCopyState(chatId, 1) {
            SourceChatId = sourceId,
            CreatedAt = new Moment(DateTime.UtcNow),
            LastCopyingAt = new Moment(DateTime.UtcNow),
            LastProcessedEntryId = 42,
            LastCorrelationId = "corr-1",
            IsCopiedSuccessfully = true,
            IsPublished = false,
        };

        var s = state.PassThroughAllSerializers(Out);
        s.Id.Should().Be(state.Id);
        s.SourceChatId.Should().Be(state.SourceChatId);
        s.LastProcessedEntryId.Should().Be(state.LastProcessedEntryId);
        s.IsCopiedSuccessfully.Should().Be(state.IsCopiedSuccessfully);
    }

    [Fact]
    public void ChatCopyStateDiff_Basic()
    {
        var diff = new ChatCopyStateDiff {
            LastProcessedEntryId = 100,
            IsCopiedSuccessfully = true,
        };
        diff.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Conversation_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var convId = ConversationId.New(chatId, 0);
        var conv = new Conversation(convId, 1) {
            Title = "Test Conversation",
            Description = "Test description",
            Summary = "A summary",
            StartsAt = new Moment(DateTime.UtcNow),
            EndsAt = new Moment(DateTime.UtcNow) + TimeSpan.FromHours(1),
            MessageCount = 42,
            IsExpandedByDefault = true,
        };

        var s = conv.PassThroughAllSerializers(Out);
        s.Id.Should().Be(conv.Id);
        s.Title.Should().Be(conv.Title);
        s.Description.Should().Be(conv.Description);
        s.Summary.Should().Be(conv.Summary);
        s.MessageCount.Should().Be(conv.MessageCount);
        s.IsExpandedByDefault.Should().BeTrue();
    }

    [Fact]
    public void ConversationDiff_Basic()
    {
        var diff = new ConversationDiff {
            Title = "Updated",
            MessageCount = 10,
            IsExpandedByDefault = true,
        };

        var s = diff.PassThroughAllSerializers(Out);
        s.Title.Should().Be(diff.Title);
        s.MessageCount.Should().Be(diff.MessageCount);
        s.IsExpandedByDefault.Should().Be(true);
    }

    [Fact]
    public void ConversationRangeMeta_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var meta = new ConversationRangeMeta(
            chatId,
            [new Range<long>(0, 100)],
            null,
            new Range<long>(100, 200));

        var s = meta.PassThroughAllSerializers(Out);
        s.ChatId.Should().Be(meta.ChatId);
        s.ConversationLidRanges.Should().BeEquivalentTo(meta.ConversationLidRanges);
        s.PreviousConversationLidRange.Should().Be(meta.PreviousConversationLidRange);
        s.NextConversationLidRange.Should().Be(meta.NextConversationLidRange);
    }

    [Fact]
    public void Place_Basic()
    {
        var placeId = PlaceId.New();
        var place = new Place(placeId, 1) {
            Title = "Test Place",
            IsPublic = true,
            CreatedAt = new Moment(DateTime.UtcNow),
            Description = "A test place",
        };

        var s = place.PassThroughAllSerializers(Out);
        s.Id.Should().Be(place.Id);
        s.Title.Should().Be(place.Title);
        s.IsPublic.Should().Be(place.IsPublic);
        s.Description.Should().Be(place.Description);
    }

    [Fact]
    public void PlaceDiff_Basic()
    {
        var diff = new PlaceDiff {
            Title = "Updated Place",
            IsPublic = false,
        };
        diff.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void PlaceRules_Basic()
    {
        var placeId = PlaceId.New();
        var rules = new PlaceRules(placeId, null, null, PlacePermissions.Write);

        var s = rules.PassThroughAllSerializers(Out);
        s.PlaceId.Should().Be(rules.PlaceId);
        s.Permissions.Should().Be(rules.Permissions);
    }

    [Fact]
    public void Role_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var roleId = RoleId.New(chatId, 1);
        var role = new Role(roleId, 1) {
            Name = "Moderator",
            Permissions = ChatPermissions.Write,
            SystemRole = SystemRole.None,
        };

        var s = role.PassThroughAllSerializers(Out);
        s.Id.Should().Be(role.Id);
        s.Name.Should().Be(role.Name);
        s.Permissions.Should().Be(role.Permissions);
    }

    [Fact]
    public void RoleDiff_Basic()
    {
        var diff = new RoleDiff {
            Name = "Admin",
            Permissions = ChatPermissions.Owner,
        };

        var s = diff.PassThroughAllSerializers(Out);
        s.Name.Should().Be(diff.Name);
        s.Permissions.Should().Be(diff.Permissions);
    }

    [Fact]
    public void Reaction_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 1);
        var authorId = AuthorId.New(chatId, 5);
        var reaction = new Reaction {
            Id = "reaction-1",
            AuthorId = authorId,
            EntryId = entryId,
            Emoji = Emojis.ThumbsUp,
            ModifiedAt = new Moment(DateTime.UtcNow),
        };

        var s = reaction.PassThroughAllSerializers(Out);
        s.Id.Should().Be(reaction.Id);
        s.AuthorId.Should().Be(reaction.AuthorId);
        s.EntryId.Should().Be(reaction.EntryId);
        s.Emoji.Should().Be(reaction.Emoji);
    }

    [Fact]
    public void ReactionSummary_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 1);
        var authorId = AuthorId.New(chatId, 5);
        var summary = new ReactionSummary {
            Id = "summary-1",
            EntryId = entryId,
            Emoji = Emojis.ThumbsUp,
            Count = 5,
            FirstAuthorIds = [authorId],
        };

        var s = summary.PassThroughAllSerializers(Out);
        s.Id.Should().Be(summary.Id);
        s.EntryId.Should().Be(summary.EntryId);
        s.Count.Should().Be(summary.Count);
    }

    [Fact]
    public void ReadPositionsStat_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var authorId = AuthorId.New(chatId, 5);
        var stat = new ReadPositionsStat(
            chatId,
            10,
            [new AuthorReadPosition(authorId, 42)]);

        var s = stat.PassThroughAllSerializers(Out);
        s.ChatId.Should().Be(stat.ChatId);
        s.StartTrackingEntryLid.Should().Be(stat.StartTrackingEntryLid);
        s.TopReadPositions.Should().BeEquivalentTo(stat.TopReadPositions);
    }

    [Fact]
    public void Mention_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 1);
        var mentionId = MentionRef.Parse("u:user123456");
        var mention = new Mention {
            Id = "mention-1",
            EntryId = entryId,
            MentionRef = mentionId,
        };

        var s = mention.PassThroughAllSerializers(Out);
        s.Id.Should().Be(mention.Id);
        s.EntryId.Should().Be(mention.EntryId);
        s.MentionRef.Should().Be(mention.MentionRef);
    }

    [Fact]
    public void SystemEntry_MembersChanged()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 1);
        var authorId = AuthorId.New(chatId, 5);
        ChatEntry entry = new MembersChangedEntry(entryId, 1) {
            TargetAuthorId = authorId,
            TargetAuthorName = "TestUser",
            HasLeft = false,
        };

        var s = entry.PassThroughModernSerializers(Out);
        s.Should().BeOfType<MembersChangedEntry>();
        var mc = (MembersChangedEntry)s;
        mc.TargetAuthorId.Should().Be(authorId);
        mc.TargetAuthorName.Should().Be("TestUser");
        mc.HasLeft.Should().BeFalse();
    }

    [Fact]
    public void SystemEntry_NotifyMembers()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 1);
        var authorId = AuthorId.New(chatId, 5);
        ChatEntry entry = new NotifyMembersEntry(entryId, 1) {
            TargetAuthorId = authorId,
            TargetAuthorName = "TestUser",
        };

        var s = entry.PassThroughModernSerializers(Out);
        s.Should().BeOfType<NotifyMembersEntry>();
        var nm = (NotifyMembersEntry)s;
        nm.TargetAuthorId.Should().Be(authorId);
        nm.TargetAuthorName.Should().Be("TestUser");
    }

    [Fact]
    public void SystemEntry_InsideChatTile()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 1);
        var authorId = AuthorId.New(chatId, 5);
        ChatEntry entry = new MembersChangedEntry(entryId, 1) {
            AuthorId = AuthorId.New(chatId, 1),
            BeginsAt = new Moment(DateTime.UtcNow),
            TargetAuthorId = authorId,
            TargetAuthorName = "TestUser",
            HasLeft = false,
        };
        var tile = new ChatTile(new Range<long>(0, 100), false, [entry]);
        var s = tile.PassThroughModernSerializers(Out);
        s.Entries.Should().HaveCount(1);
        var rt = s.Entries[0];
        rt.Should().BeOfType<MembersChangedEntry>();
        var mc = (MembersChangedEntry)rt;
        mc.TargetAuthorId.Should().Be(authorId);
        mc.TargetAuthorName.Should().Be("TestUser");
        mc.HasLeft.Should().BeFalse();
    }

    [Fact]
    public void SystemEntry_RoundTripsViaDbJson()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 1);
        var authorId = AuthorId.New(chatId, 5);
        ChatEntry entry = new MembersChangedEntry(entryId, 1) {
            AuthorId = AuthorId.New(chatId, 1),
            BeginsAt = new Moment(DateTime.UtcNow),
            TargetAuthorId = authorId,
            TargetAuthorName = "TestUser",
            HasLeft = false,
        };
        var dbEntry = new ActualChat.Chat.Db.DbChatEntry(entry);
        Out.WriteLine($"DbEntry.Content = {dbEntry.Content}");
        Out.WriteLine($"DbEntry.IsSystemEntry = {dbEntry.IsSystemEntry}");
        var roundTripped = dbEntry.ToModel();
        roundTripped.Should().BeOfType<MembersChangedEntry>();
        var mc = (MembersChangedEntry)roundTripped;
        mc.TargetAuthorId.Should().Be(authorId);
        mc.TargetAuthorName.Should().Be("TestUser");
        mc.HasLeft.Should().BeFalse();
    }

    [Fact]
    public void ChatEntryAttachment_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var entryId = ChatEntryId.New(chatId, 1);
        var mediaId = MediaId.New("scope1");
        var attachment = new ChatEntryAttachment("att-1", 1) {
            EntryId = entryId,
            Index = 0,
            MediaId = mediaId,
        };

        var s = attachment.PassThroughAllSerializers(Out);
        s.Id.Should().Be(attachment.Id);
        s.EntryId.Should().Be(attachment.EntryId);
        s.Index.Should().Be(attachment.Index);
        s.MediaId.Should().Be(attachment.MediaId);
    }

    [Fact]
    public void Translation_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var sourceId = TranslationSourceId.New(ChatEntryId.New(chatId, 1));
        var translationId = TranslationId.New(sourceId, Languages.Russian);
        var translation = new Translation(translationId, 1) {
            Content = "Привет, мир!",
            CreatedAt = new Moment(DateTime.UtcNow),
            ModifiedAt = new Moment(DateTime.UtcNow),
        };

        var s = translation.PassThroughAllSerializers(Out);
        s.Id.Should().Be(translation.Id);
        s.Content.Should().Be(translation.Content);
    }

    [Fact]
    public void TranslationDiff_Basic()
    {
        var diff = new TranslationDiff {
            Content = "Updated translation",
            Version = 2,
        };
        diff.AssertPassesThroughAllSerializers();
    }
}
