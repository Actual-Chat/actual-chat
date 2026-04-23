using System.Buffers;
using ActualChat.Aot;
using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using FlowData = ActualChat.Flows.Infrastructure.FlowData;

namespace ActualChat.Core.Server.UnitTests;

public class SerializationCodeGenTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        SerializationCodeGen.ValidateType<Change<string>>();
        SerializationCodeGen.ValidateType<FlowReadiness>();
        SerializationCodeGen.ValidateType<FlowId>();
        SerializationCodeGen.ValidateType<FlowData>();
        SerializationCodeGen.ValidateType<FlowResumeEvent>();
        SerializationCodeGen.ValidateType<IndexingFlowCursor<ChatId>>();
    }

    // AOT/trimming sanity check: every AotTypes.All entry marked Serializable must resolve
    // to a usable converter via Serializers.ClientSide — the codegen-only state with NO
    // reflection fallback. A type that only resolves via PolyType's reflection provider would
    // compile and round-trip on the server but fail on AOT clients (Wasm / MAUI / NativeAOT)
    // the moment a payload of that type hits the wire.
    [Fact]
    public void AllSerializableTypes_ResolveOn_ClientSide_MessagePack()
        => AssertAllResolve(Serializers.ClientSide.MessagePack, "ClientSide.MessagePack");

    [Fact]
    public void AllSerializableTypes_ResolveOn_ClientSide_KeylessMessagePack()
        => AssertAllResolve(Serializers.ClientSide.KeylessMessagePack, "ClientSide.KeylessMessagePack");

    private void AssertAllResolve(IByteSerializer serializer, string label)
    {
        var supported = 0;
        var missing = new List<string>();
        var buffer = new ArrayBufferWriter<byte>(64);

        foreach (var type in SerializableTypes()) {
            try {
                buffer.ResetWrittenCount();
                // Force converter resolution: Write needs a shape for `type` regardless of
                // whether the value is null. A reference type writes nil (1 byte), a value
                // type writes its default-instance form. Either way, a missing shape on the
                // codegen-only provider throws here — exactly what AOT/Wasm/Maui would hit.
                var value = type.IsValueType ? Activator.CreateInstance(type) : null;
                serializer.Write(buffer, value, type);
                supported++;
            }
            catch (Exception e) {
                var inner = e.GetBaseException();
                missing.Add($"{type.FullName}: {inner.GetType().Name}: {inner.Message}");
            }
        }

        Out.WriteLine($"{label}: {supported} OK, {missing.Count} missing");
        missing.Count.Should().Be(0,
            "\n  " + string.Join("\n  ", missing) +
            $"\n— every Serializable AotTypes entry must resolve to a non-reflection {label} converter.");
    }

    private static IEnumerable<Type> SerializableTypes()
        => AotTypes.All
            .Where(kv => kv.Value == AotTypeKind.Serializable)
            .Select(kv => kv.Key)
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });
}
