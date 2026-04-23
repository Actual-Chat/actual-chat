// PolyType witness covering Fusion-side framework types that ActualChat serializes through
// Write(buffer, value, type) paths but doesn't otherwise reach via the project's own
// [GenerateShapeFor<T>] declarations. Enumerated explicitly here so codegen produces shapes
// instead of falling back to reflection at runtime.
//
// TypeDecoratingByteSerializer writes a TypeRef per envelope, Moment is a near-universal
// timestamp, ExceptionInfo wraps remote errors in Result<T> payloads, etc. — these flow
// through almost every binary serialization call site.
//
// Add a new entry whenever a new Fusion framework type starts appearing in
// "type ... has no shape" / null-shape failures.

using ActualLab.IO;
using ActualLab.Rpc;
using ActualLab.Rpc.Caching;
using ActualLab.Rpc.Infrastructure;
using ActualLab.Serialization.Internal;

namespace ActualChat.Serialization;

[GenerateShapeFor<TypeRef>]
[GenerateShapeFor<ByteString>]
[GenerateShapeFor<Moment>]
[GenerateShapeFor<HostId>]
[GenerateShapeFor<FilePath>]
[GenerateShapeFor<JsonString>]
[GenerateShapeFor<Symbol>]
[GenerateShapeFor<CpuTimestamp>]
[GenerateShapeFor<Unit>]
[GenerateShapeFor<Session>]
[GenerateShapeFor<ExceptionInfo>]
[GenerateShapeFor<VersionSet>]
[GenerateShapeFor<RpcHandshake>]
[GenerateShapeFor<RpcObjectId>]
[GenerateShapeFor<RpcHeader>]
[GenerateShapeFor<RpcHeaderKey>]
[GenerateShapeFor<RpcMethodRef>]
[GenerateShapeFor<RpcCacheKey>]
[GenerateShapeFor<RpcCacheValue>]
[GenerateShapeFor<PropertyBag>]
[GenerateShapeFor<MutablePropertyBag>]
[GenerateShapeFor<ImmutableOptionSet>]
[GenerateShapeFor<MessagePackData>]
// Fusion's $sys.* system RPC methods carry these arg types — all closed in System.* /
// ActualLab.* assemblies, so neither AotHelper's serializable scan nor the API signature
// scan (which only walks ActualChat-declared interfaces) picks them up.
//   $sys.Reconnect       → Dictionary<int, byte[]>
//   $sys.KeepAlive       → long[]
//   $sys.Disconnect      → long[]
[GenerateShapeFor<Dictionary<int, byte[]>>]
[GenerateShapeFor<long[]>]
internal partial class FrameworkWitness;
