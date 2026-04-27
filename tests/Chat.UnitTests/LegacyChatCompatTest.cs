using ActualChat.Hashing;

namespace ActualChat.Chat.UnitTests;

/// <summary>
/// Pins down the v2.7 wire-frozen <see cref="LegacyChatEntry"/> / <see cref="LegacySystemEntry"/>
/// shapes and the modern -> legacy projection used by <see cref="LegacyChats"/>.
/// </summary>
public class LegacyChatCompatTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly ChatEntryId TestEntryId = ChatEntryId.New(TestChatId, 123);
    private static readonly AuthorId BotAuthorId = AuthorId.New(TestChatId, 1);
    private static readonly AuthorId UserAuthorId = AuthorId.New(TestChatId, 5);

    [Fact]
    public void LegacyChatEntry_RoundTripsViaMemoryPack()
    {
        var legacy = new LegacyChatEntry(TestEntryId, 42) {
            Flags = ChatEntryFlags.IsThreadStart,
            AuthorId = BotAuthorId,
            BeginsAt = new Moment(DateTime.UtcNow),
            EndsAt = new Moment(DateTime.UtcNow + TimeSpan.FromSeconds(2)),
            Content = "hello",
            ContentHash = HashString.None,
            ContentStreamId = "",
            ClientId = "client-1",
            LinkPreviewIds = [(Symbol)"lp-1"],
        };

        var roundTripped = legacy.PassThroughMemoryPackByteSerializer(Out);
        roundTripped.Id.Should().Be(legacy.Id);
        roundTripped.Version.Should().Be(legacy.Version);
        roundTripped.Flags.Should().Be(legacy.Flags);
        roundTripped.AuthorId.Should().Be(legacy.AuthorId);
        roundTripped.Content.Should().Be(legacy.Content);
        roundTripped.ContentHash.Should().Be(legacy.ContentHash);
        roundTripped.ClientId.Should().Be(legacy.ClientId);
        roundTripped.LinkPreviewIds.Should().BeEquivalentTo(legacy.LinkPreviewIds);
    }

    [Fact]
    public void LegacySystemEntry_MembersChanged_RoundTripsViaMemoryPack()
    {
        var legacy = new LegacySystemEntry {
            Option = new LegacyMembersChangedOption(UserAuthorId, "Alice", hasLeft: false),
        };

        var roundTripped = legacy.PassThroughMemoryPackByteSerializer(Out);
        roundTripped.MembersChanged.Should().NotBeNull();
        roundTripped.MembersChanged!.AuthorId.Should().Be(UserAuthorId);
        roundTripped.MembersChanged.AuthorName.Should().Be("Alice");
        roundTripped.MembersChanged.HasLeft.Should().BeFalse();
    }

    [Fact]
    public void LegacySystemEntry_NotifyMembers_RoundTripsViaMemoryPack()
    {
        var legacy = new LegacySystemEntry {
            Option = new LegacyNotifyMembersOption(UserAuthorId, "Alice"),
        };

        var roundTripped = legacy.PassThroughMemoryPackByteSerializer(Out);
        roundTripped.NotifyMembers.Should().NotBeNull();
        roundTripped.NotifyMembers!.AuthorId.Should().Be(UserAuthorId);
        roundTripped.NotifyMembers.AuthorName.Should().Be("Alice");
    }

    [Fact]
    public void TextEntry_ConvertsToLegacyChatEntry()
    {
        ChatEntry modern = new TextEntry(TestEntryId, 7) {
            AuthorId = UserAuthorId,
            BeginsAt = new Moment(DateTime.UtcNow),
            Content = "hello world",
            ContentHash = HashString.None,
            ClientId = "client-1",
        };

        var legacy = LegacyChatEntry.From(modern);
        legacy.Id.Should().Be(TestEntryId);
        legacy.Version.Should().Be(7);
        legacy.AuthorId.Should().Be(UserAuthorId);
        legacy.Content.Should().Be("hello world");
        legacy.SystemEntry.Should().BeNull();
        legacy.ClientId.Should().Be("client-1");
    }

    [Fact]
    public void MembersChangedEntry_ConvertsToLegacyWithWrapperShape()
    {
        ChatEntry modern = new MembersChangedEntry(TestEntryId, 7) {
            AuthorId = BotAuthorId,
            BeginsAt = new Moment(DateTime.UtcNow),
            TargetAuthorId = UserAuthorId,
            TargetAuthorName = "Alice",
            HasLeft = true,
        };

        var legacy = LegacyChatEntry.From(modern);
        legacy.SystemEntry.Should().NotBeNull();
        legacy.SystemEntry!.MembersChanged.Should().NotBeNull();
        legacy.SystemEntry.MembersChanged!.AuthorId.Should().Be(UserAuthorId);
        legacy.SystemEntry.MembersChanged.AuthorName.Should().Be("Alice");
        legacy.SystemEntry.MembersChanged.HasLeft.Should().BeTrue();
    }

    [Fact]
    public void NotifyMembersEntry_ConvertsToLegacyWithWrapperShape()
    {
        ChatEntry modern = new NotifyMembersEntry(TestEntryId, 7) {
            AuthorId = BotAuthorId,
            BeginsAt = new Moment(DateTime.UtcNow),
            TargetAuthorId = UserAuthorId,
            TargetAuthorName = "Alice",
        };

        var legacy = LegacyChatEntry.From(modern);
        legacy.SystemEntry.Should().NotBeNull();
        legacy.SystemEntry!.NotifyMembers.Should().NotBeNull();
        legacy.SystemEntry.NotifyMembers!.AuthorId.Should().Be(UserAuthorId);
        legacy.SystemEntry.NotifyMembers.AuthorName.Should().Be("Alice");
    }

    [Fact]
    public void LegacyChatNews_ConvertsLastTextEntry()
    {
        ChatEntry modern = new TextEntry(TestEntryId, 7) {
            AuthorId = UserAuthorId,
            BeginsAt = new Moment(DateTime.UtcNow),
            Content = "tail",
        };
        var modernNews = new ChatNews(new Range<long>(1, 100), modern);

        var legacy = LegacyChatNews.From(modernNews);
        legacy.Should().NotBeNull();
        legacy!.TextEntryIdRange.Should().Be(modernNews.TextEntryIdRange);
        legacy.LastTextEntry.Should().NotBeNull();
        legacy.LastTextEntry!.Content.Should().Be("tail");
    }

    [Fact]
    public void LegacyChatTile_ConvertsAllEntries()
    {
        ChatEntry text = new TextEntry(TestEntryId, 1) { AuthorId = UserAuthorId, BeginsAt = default, Content = "x" };
        ChatEntry sys = new MembersChangedEntry(ChatEntryId.New(TestChatId, 124), 1) {
            AuthorId = BotAuthorId,
            BeginsAt = default,
            TargetAuthorId = UserAuthorId,
            TargetAuthorName = "Alice",
            HasLeft = false,
        };
        var modernTile = new ChatTile(new Range<long>(123, 125), false, [text, sys]);

        var legacy = LegacyChatTile.From(modernTile);
        legacy.Entries.Should().HaveCount(2);
        legacy.Entries[0].Content.Should().Be("x");
        legacy.Entries[0].SystemEntry.Should().BeNull();
        legacy.Entries[1].SystemEntry.Should().NotBeNull();
        legacy.Entries[1].SystemEntry!.MembersChanged.Should().NotBeNull();
    }
}
