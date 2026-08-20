using System.Buffers;
using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;
using ActualLab.Rpc.Serialization;

namespace ActualChat.Serialization;

/// <summary>
/// Inbound RPC argument decorator that migrates legacy <see cref="ApiCommand"/> payloads from peers older
/// than <see cref="UuidVersion"/> (Session @ 0) to the current layout (Uuid @ 0) by prepending an empty Uuid.
/// Newer peers, backend peers, and non-command args pass through the inner V4 verbatim.
/// </summary>
public sealed class ApiCommandRpcArgumentSerializer : RpcArgumentSerializer
{
    // The API version (from the RPC handshake's per-peer RemoteApiVersionSet) that introduces ApiCommand.Uuid.
    // Pinned to the release this ships in, NOT ApiConstants.Version, which floats forward with every build.
    public static readonly Version UuidVersion = new(2, 16);

    private readonly IByteSerializer _baseSerializer;
    private readonly RpcByteArgumentSerializerV4 _inner;
    private readonly Func<Version?> _peerVersionSource;

    // peerVersionSource is injectable so tests can drive the legacy branch without a live RPC peer.
    public ApiCommandRpcArgumentSerializer(IByteSerializer baseSerializer, Func<Version?>? peerVersionSource = null)
    {
        _baseSerializer = baseSerializer;
        _inner = new RpcByteArgumentSerializerV4(baseSerializer);
        _peerVersionSource = peerVersionSource ?? GetInboundApiVersion;
    }

    public override void Serialize(ArgumentList arguments, bool needsPolymorphism, ArrayPoolBuffer<byte> buffer)
        => _inner.Serialize(arguments, needsPolymorphism, buffer);

    public override void Deserialize(ref ArgumentList arguments, bool needsPolymorphism, ReadOnlyMemory<byte> data)
    {
        var peerVersion = _peerVersionSource();
        if (needsPolymorphism || peerVersion is null || peerVersion >= UuidVersion) {
            _inner.Deserialize(ref arguments, needsPolymorphism, data);
            return;
        }

        var itemTypes = arguments.Type.ItemTypes;
        for (var i = 0; i < itemTypes.Length; i++) {
            var type = itemTypes[i];
            if (type == typeof(CancellationToken)) {
                arguments.SetCancellationToken(i, default);
                continue;
            }
            var item = typeof(ApiCommand).IsAssignableFrom(type)
                ? ReadMigratedCommand(ref data, type)
                : _baseSerializer.Read(ref data, type);
            arguments.SetUntyped(i, item);
        }
    }

    // Private methods

    private object? ReadMigratedCommand(ref ReadOnlyMemory<byte> data, Type type)
    {
        var reader = new MessagePackReader(data);
        reader.Skip();
        var consumed = checked((int)reader.Consumed);
        var migrated = (ReadOnlyMemory<byte>)PrependUuid(data.Slice(0, consumed));
        data = data.Slice(consumed);
        return _baseSerializer.Read(ref migrated, type);
    }

    private static byte[] PrependUuid(ReadOnlyMemory<byte> legacyData)
    {
        var reader = new MessagePackReader(legacyData);
        var count = reader.ReadArrayHeader();

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(count + 1);
        writer.Write(""); // Uuid — absent in the legacy payload
        for (var i = 0; i < count; i++)
            writer.WriteRaw(reader.ReadRaw());
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static Version? GetInboundApiVersion()
    {
        var peer = RpcInboundContext.Current?.Peer;
        var version = peer?.ConnectionState.Value.Handshake?.RemoteApiVersionSet?[RpcDefaults.ApiScope];
        // Absent Api scope (backend peer, or no handshake) reads as ZeroVersion — treat as "no info", not legacy.
        return version == VersionSet.ZeroVersion ? null : version;
    }
}
