using System.Buffers;
using ActualChat.Hashing;
using ActualChat.Notifications;
using ActualChat.Serialization.Internal;
using MessagePack;

namespace ActualChat.Chat.UnitTests;

public class ForwardCompatibleUnionTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const int UnknownMessageTag = 50;
    private const int UnknownSystemTag = 105;

    private static readonly MessagePackSerializerOptions KeyedOptions = new (AppMessagePackResolver.Instance);
    private static readonly MessagePackSerializerOptions KeylessOptions = new (AppMessagePackKeylessResolver.Instance);
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KnownTagsShouldRoundTripUnchanged(bool isKeyed)
    {
        var options = Options(isKeyed);
        var textEntry = RoundTrip<ChatEntry>(NewTextEntry(), options);
        textEntry.Should().BeOfType<TextEntry>();
        textEntry.Id.Should().Be(NewTextEntry().Id);
        textEntry.Flags.Should().Be(ChatEntryFlags.IsRemoved);
        textEntry.Content.Should().Be("Hello, world!");

        var systemEntry = RoundTrip<ChatEntry>(NewMembersChangedEntry(), options);
        systemEntry.Should().BeOfType<MembersChangedEntry>();
        systemEntry.IsSystemEntry.Should().BeTrue();
        ((MembersChangedEntry)systemEntry).TargetAuthorName.Should().Be("Someone");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownMessageTagShouldBecomeAnUnsupportedTextEntry(bool isKeyed)
    {
        // arrange
        var options = Options(isKeyed);
        var bytes = WithTag(MessagePackSerializer.Serialize<ChatEntry>(NewTextEntry(), options), UnknownMessageTag);

        // act
        var entry = MessagePackSerializer.Deserialize<ChatEntry>(bytes, options);

        // assert
        entry.Should().BeOfType<TextEntry>();
        entry.IsSystemEntry.Should().BeFalse();
        entry.Flags.Should().HaveFlag(ChatEntryFlags.IsUnsupported);
        entry.Flags.Should().HaveFlag(ChatEntryFlags.IsRemoved, "the recovered prefix carries the flags");
        entry.Id.Should().Be(NewTextEntry().Id, "a tile is keyed and ordered by entry id");
        entry.Version.Should().Be(7);
        entry.AuthorId.Should().Be(AuthorId.New(TestChatId, 10));
        entry.Content.Should().BeEmpty("an unsupported entry's content is not ours to render");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownSystemTagShouldBecomeAnUnsupportedSystemEntry(bool isKeyed)
    {
        // arrange
        var options = Options(isKeyed);
        var bytes = WithTag(MessagePackSerializer.Serialize<ChatEntry>(NewTextEntry(), options), UnknownSystemTag);

        // act
        var entry = MessagePackSerializer.Deserialize<ChatEntry>(bytes, options);

        // assert
        entry.Should().BeOfType<UnsupportedSystemEntry>();
        entry.IsSystemEntry.Should().BeTrue("the consumers that skip system entries must skip this one too");
        entry.Flags.Should().HaveFlag(ChatEntryFlags.IsUnsupported);
        entry.Id.Should().Be(NewTextEntry().Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnknownEntryShouldNotBreakTheTileAroundIt(bool isKeyed)
    {
        // arrange
        var options = Options(isKeyed);
        var known = MessagePackSerializer.Serialize<ChatEntry>(NewTextEntry(), options);
        var unknown = WithTag(known, UnknownSystemTag);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(3);
        writer.Flush();
        var bytes = Concat(buffer.WrittenSpan.ToArray(), known, unknown, known);

        // act
        var entries = MessagePackSerializer.Deserialize<ChatEntry[]>(bytes, options);

        // assert
        entries.Should().HaveCount(3);
        entries[0].Should().BeOfType<TextEntry>();
        entries[1].Should().BeOfType<UnsupportedSystemEntry>();
        entries[2].Should().BeOfType<TextEntry>();
        entries[2].Content.Should().Be("Hello, world!", "the entry after the unknown one is read normally");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CorruptionWithAKnownTagShouldStillThrow(bool isKeyed)
    {
        // arrange
        var options = Options(isKeyed);
        var bytes = MessagePackSerializer.Serialize<ChatEntry>(NewTextEntry(), options);
        var truncated = bytes[..(bytes.Length / 2)];

        // act & assert
        var act = () => MessagePackSerializer.Deserialize<ChatEntry>(truncated, options);
        act.Should().Throw<MessagePackSerializationException>(
            "tolerance is for unknown tags only - corruption must stay an error");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownMarkupShouldContributeNothing(bool isKeyed)
    {
        // arrange
        var options = Options(isKeyed);
        var bytes = WithTag(MessagePackSerializer.Serialize<Markup>(new PlainTextMarkup("Hi"), options), 99);

        // act
        var markup = MessagePackSerializer.Deserialize<Markup>(bytes, options);

        // assert
        markup.Should().BeSameAs(PlainTextMarkup.Empty);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownNotificationShouldBecomeNull(bool isKeyed)
    {
        // arrange
        var options = Options(isKeyed);
        var notificationId = NotificationId.New(UserId.Parse("dmitrii"), NotificationKind.Message, "the-actual-one");
        var notification = new MessageNotification(notificationId);
        var bytes = WithTag(MessagePackSerializer.Serialize<Notifications.Notification>(notification, options), 99);

        // act
        var result = MessagePackSerializer.Deserialize<Notifications.Notification>(bytes, options);

        // assert
        result.Should().BeNull();
    }

    [Fact]
    public void EverySystemMemberShouldCarryASystemTag()
    {
        // act
        var members = typeof(ChatEntry).GetCustomAttributes<UnionAttribute>().ToList();

        // assert
        members.Should().NotBeEmpty();
        foreach (var member in members)
            ChatEntry.IsSystemUnionTag(member.Key)
                .Should()
                .Be(typeof(SystemEntry).IsAssignableFrom(member.SubType),
                    $"{member.SubType.Name} is tagged {member.Key}, and tags "
                    + $"{ChatEntry.FirstSystemUnionTag}..{ChatEntry.LastSystemUnionTag} are the system-entry range");
    }

    // Private methods

    private static MessagePackSerializerOptions Options(bool isKeyed)
        => isKeyed ? KeyedOptions : KeylessOptions;

    private static T RoundTrip<T>(T value, MessagePackSerializerOptions options)
        => MessagePackSerializer.Deserialize<T>(MessagePackSerializer.Serialize(value, options), options);

    private static byte[] WithTag(byte[] envelope, int tag)
    {
        var reader = new MessagePackReader(envelope);
        reader.ReadArrayHeader();
        reader.ReadInt32();
        var payload = envelope[(int)reader.Consumed..];
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(2);
        writer.Write(tag);
        writer.Flush();
        return Concat(buffer.WrittenSpan.ToArray(), payload);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(x => x.Length)];
        var offset = 0;
        foreach (var part in parts) {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }

    private static TextEntry NewTextEntry()
        => new (ChatEntryId.New(TestChatId, 1), 7) {
            AuthorId = AuthorId.New(TestChatId, 10),
            BeginsAt = new Moment(DateTime.UnixEpoch),
            Content = "Hello, world!",
            ContentHash = HashString.None,
            Flags = ChatEntryFlags.IsRemoved,
        };

    private static MembersChangedEntry NewMembersChangedEntry()
        => new (ChatEntryId.New(TestChatId, 2), 8) {
            AuthorId = AuthorId.New(TestChatId, -1),
            BeginsAt = new Moment(DateTime.UnixEpoch),
            ContentHash = HashString.None,
            TargetAuthorId = AuthorId.New(TestChatId, 10),
            TargetAuthorName = "Someone",
        };
}
