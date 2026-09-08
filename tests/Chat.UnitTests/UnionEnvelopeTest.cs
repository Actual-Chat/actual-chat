using ActualChat.Hashing;
using ActualChat.Serialization.Internal;
using MessagePack;

namespace ActualChat.Chat.UnitTests;

/// <summary>
/// Pins the wire shape a tolerant union formatter depends on: a union value is a
/// two-element <c>[tag, payload]</c> array, and <see cref="MessagePackReader.Skip"/>
/// walks it whole without knowing the payload's type.
/// </summary>
public class UnionEnvelopeTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly MessagePackSerializerOptions KeyedOptions = new (AppMessagePackResolver.Instance);
    private static readonly MessagePackSerializerOptions KeylessOptions = new (AppMessagePackKeylessResolver.Instance);
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void TextEntryShouldBeWrappedInAnEnvelope()
        => Dump(NewTextEntry());

    [Fact]
    public void MembersChangedEntryShouldBeWrappedInAnEnvelope()
        => Dump(NewMembersChangedEntry());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SkipShouldWalkTheWholeEnvelope(bool isKeyed)
    {
        // arrange
        var options = isKeyed ? KeyedOptions : KeylessOptions;
        var bytes = MessagePackSerializer.Serialize<ChatEntry>(NewTextEntry(), options);

        // act
        var peek = new MessagePackReader(bytes);
        var count = peek.ReadArrayHeader();
        var tag = peek.ReadInt32();
        var reader = new MessagePackReader(bytes);
        reader.Skip();

        // assert
        Out.WriteLine($"envelope: count={count}, tag={tag}");
        count.Should().Be(2, "the tolerant formatter reads the tag out of a [tag, payload] array");
        tag.Should().Be(0, "TextEntry is [Union(0)] on ChatEntry");
        reader.End.Should().BeTrue("Skip must walk an envelope whose payload type is unknown");
    }

    // Private methods

    private void Dump(ChatEntry entry)
    {
        foreach (var (name, options) in new[] { ("keyed", KeyedOptions), ("keyless", KeylessOptions) }) {
            Write($"{name} as {entry.GetType().Name}", entry, options);
            Write($"{name} as ChatEntry", (ChatEntry)entry, options);
            if (entry is SystemEntry systemEntry)
                Write($"{name} as SystemEntry", systemEntry, options);
        }
    }

    private void Write<T>(string title, T value, MessagePackSerializerOptions options)
    {
        var bytes = MessagePackSerializer.Serialize(value, options);
        Out.WriteLine($"{title}: {MessagePackSerializer.ConvertToJson(bytes, options)}");
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
