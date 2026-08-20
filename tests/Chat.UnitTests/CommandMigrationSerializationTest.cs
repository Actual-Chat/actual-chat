using System.Buffers;
using ActualLab.Interception;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Chat.UnitTests;

/// <summary>
/// Backward-compatible command deserializer gated by the sender's protocol version: a peer older than
/// <see cref="UuidVersion"/> sends the legacy layout (Session @ 0), which is migrated to the current
/// <see cref="ApiCommand"/> layout (Uuid @ 0) by prepending an empty Uuid; newer peers send it as-is.
/// </summary>
public static class ApiCommandSerializer
{
    // The API version (from the RPC handshake's RemoteApiVersionSet) where the Uuid field was introduced.
    // Pinned to the shipping release (2.16), NOT derived from ApiConstants.Version, which floats forward.
    // Deliberately explicit, not sniffed from the payload: version-tolerant layouts can share an element
    // count, so length alone can't tell the formats apart — the peer's handshake version can.
    public static readonly Version UuidVersion = new(2, 16);

    public static byte[] Serialize<T>(T command, MessagePackSerializerOptions options)
        where T : ApiCommand
        => MessagePackSerializer.Serialize(command, options);

    public static T Deserialize<T>(ReadOnlyMemory<byte> data, Version peerVersion, MessagePackSerializerOptions options)
        where T : ApiCommand
        => (T)Deserialize(data, typeof(T), peerVersion, options);

    public static object Deserialize(
        ReadOnlyMemory<byte> data, Type commandType, Version peerVersion, MessagePackSerializerOptions options)
    {
        var newFormatData = peerVersion < UuidVersion ? PrependUuid(data) : data;
        return MessagePackSerializer.Deserialize(commandType, newFormatData, options)!;
    }

    private static byte[] PrependUuid(ReadOnlyMemory<byte> legacyData)
    {
        var reader = new MessagePackReader(legacyData);
        var count = reader.ReadArrayHeader();

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(count + 1);
        writer.Write(""); // Uuid (absent in the legacy payload)
        for (var i = 0; i < count; i++)
            writer.WriteRaw(reader.ReadRaw());
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }
}

public class CommandMigrationSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Session TestSession = Session.New();
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly PlaceId TestPlaceId = PlaceId.New();
    private static readonly Version LegacyPeer = new(2, 15); // previous release, just below UuidVersion
    private static readonly Version CurrentPeer = ApiCommandSerializer.UuidVersion; // 2.16, at the boundary
    private static MessagePackSerializerOptions Options => MessagePackByteSerializer.DefaultOptions;

    [Theory]
    [InlineData(true)] // old peer -> legacy layout
    [InlineData(false)] // current peer -> ApiCommand layout
    public void PeerVersionSelectsCopyChatFormat(bool legacyPeer)
    {
        // arrange
        var peerVersion = legacyPeer ? LegacyPeer : CurrentPeer;
        var bytes = legacyPeer
            ? MessagePackSerializer.Serialize(
                new TstCmd_LegacyCopyChat(TestSession, TestChatId, TestPlaceId, "corr-1"), Options)
            : ApiCommandSerializer.Serialize(
                new TstCmd_CopyChat {
                    Uuid = "uuid-1", Session = TestSession,
                    SourceChatId = TestChatId, PlaceId = TestPlaceId, CorrelationId = "corr-1",
                }, Options);
        Out.WriteLine($"peer={peerVersion}: {MessagePackSerializer.ConvertToJson(bytes, Options)}");

        // act
        var result = ApiCommandSerializer.Deserialize<TstCmd_CopyChat>(bytes, peerVersion, Options);

        // assert
        result.Uuid.Should().Be(legacyPeer ? "" : "uuid-1");
        result.Session.Should().Be(TestSession);
        result.SourceChatId.Should().Be(TestChatId);
        result.PlaceId.Should().Be(TestPlaceId);
        result.CorrelationId.Should().Be("corr-1");
    }

    [Theory]
    [InlineData(true)] // old peer -> legacy layout
    [InlineData(false)] // current peer -> ApiCommand layout
    public void PeerVersionSelectsRemoveEntriesFormat(bool legacyPeer)
    {
        // arrange
        var peerVersion = legacyPeer ? LegacyPeer : CurrentPeer;
        var bytes = legacyPeer
            ? MessagePackSerializer.Serialize(
                new TstCmd_LegacyRemoveEntries(TestSession, TestChatId, [1L, 2L, 3L]), Options)
            : ApiCommandSerializer.Serialize(
                new TstCmd_RemoveEntries {
                    Uuid = "uuid-2", Session = TestSession, ChatId = TestChatId, LocalIds = [1L, 2L, 3L],
                }, Options);

        // act
        var result = ApiCommandSerializer.Deserialize<TstCmd_RemoveEntries>(bytes, peerVersion, Options);

        // assert
        result.Uuid.Should().Be(legacyPeer ? "" : "uuid-2");
        result.Session.Should().Be(TestSession);
        result.ChatId.Should().Be(TestChatId);
        result.LocalIds.Should().Equal(1L, 2L, 3L);
    }
}

public class ApiCommandRpcArgumentSerializerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly IByteSerializer Base = MessagePackByteSerializer.Default;
    private static readonly Session TestSession = Session.New();
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static MessagePackSerializerOptions Options => MessagePackByteSerializer.DefaultOptions;

    [Theory]
    [InlineData(true)] // old peer -> legacy bytes, decorator prepends an empty Uuid
    [InlineData(false)] // current peer -> new bytes, decorator passes through
    public void MigratesLegacyCommandArgByPeerVersion(bool legacyPeer)
    {
        // arrange
        var peerVersion = legacyPeer ? new Version(2, 10) : ApiCommandRpcArgumentSerializer.UuidVersion;
        var data = legacyPeer
            ? (ReadOnlyMemory<byte>)MessagePackSerializer.Serialize(
                new TstCmd_LegacyRemoveEntries(TestSession, TestChatId, [1L, 2L, 3L]), Options)
            : MessagePackSerializer.Serialize(
                new TstCmd_RemoveEntries {
                    Uuid = "uuid-9", Session = TestSession, ChatId = TestChatId, LocalIds = [1L, 2L, 3L],
                }, Options);
        var serializer = new ApiCommandRpcArgumentSerializer(Base, () => peerVersion);
        ArgumentList args = ArgumentList.New(default(TstCmd_RemoveEntries)!);

        // act
        serializer.Deserialize(ref args, false, data);
        var command = (TstCmd_RemoveEntries)args.GetUntyped(0)!;

        // assert
        command.Uuid.Should().Be(legacyPeer ? "" : "uuid-9");
        command.Session.Should().Be(TestSession);
        command.ChatId.Should().Be(TestChatId);
        command.LocalIds.Should().Equal(1L, 2L, 3L);
    }

    [Fact]
    public void NullPeerVersionPassesThrough()
    {
        // arrange — no peer info (e.g. a local call) must not touch a current-layout payload
        var data = (ReadOnlyMemory<byte>)MessagePackSerializer.Serialize(
            new TstCmd_RemoveEntries {
                Uuid = "uuid-x", Session = TestSession, ChatId = TestChatId, LocalIds = [7L],
            }, Options);
        var serializer = new ApiCommandRpcArgumentSerializer(Base, () => null);
        ArgumentList args = ArgumentList.New(default(TstCmd_RemoveEntries)!);

        // act
        serializer.Deserialize(ref args, false, data);
        var command = (TstCmd_RemoveEntries)args.GetUntyped(0)!;

        // assert
        command.Uuid.Should().Be("uuid-x");
        command.LocalIds.Should().Equal(7L);
    }

    [Fact]
    public void DefaultSourceOutsideRpcPassesThrough()
    {
        // arrange — the default version source reads RpcInboundContext.Current, which is null off the RPC path
        RpcInboundContext.Current.Should().BeNull();
        var data = (ReadOnlyMemory<byte>)MessagePackSerializer.Serialize(
            new TstCmd_RemoveEntries {
                Uuid = "uuid-local", Session = TestSession, ChatId = TestChatId, LocalIds = [5L],
            }, Options);
        var serializer = new ApiCommandRpcArgumentSerializer(Base);
        ArgumentList args = ArgumentList.New(default(TstCmd_RemoveEntries)!);

        // act
        serializer.Deserialize(ref args, false, data);
        var command = (TstCmd_RemoveEntries)args.GetUntyped(0)!;

        // assert
        command.Uuid.Should().Be("uuid-local");
        command.LocalIds.Should().Equal(5L);
    }
}

// Legacy commands: Session @ 0, payload packed from index 1.

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record TstCmd_LegacyCopyChat(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId SourceChatId,
    [property: DataMember, Key(2)] PlaceId PlaceId,
    [property: DataMember, Key(3)] string CorrelationId
) : ISessionCommand<Chat_CopyChatResult>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record TstCmd_LegacyRemoveEntries(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] long[] LocalIds
) : ISessionCommand<Unit>, IApiCommand;

// New commands: ApiCommand-derived (Uuid @ 0, Session @ 1), own payload shifted by +1 from index 2.

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record TstCmd_CopyChat : ApiCommand<Chat_CopyChatResult>
{
    [DataMember(Order = 2), Key(2)] public required ChatId SourceChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required PlaceId PlaceId { get; init; }
    [DataMember(Order = 4), Key(4)] public required string CorrelationId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record TstCmd_RemoveEntries : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long[] LocalIds { get; init; }
}
