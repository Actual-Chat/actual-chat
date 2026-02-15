# RPC Method Hashes

ActualLab.Fusion's RPC protocols with "c" suffix ("compact") identify methods by hash codes rather than by string names. 
This is important to understand when maintaining backwards compatibility with old clients (e.g., legacy API stubs).

## How Method Full Names Are Formed

Each RPC method gets a **full name** composed of three parts:

```
{ServiceName}.{MethodName}:{ParameterCount}
```

- **ServiceName** — the interface type name (e.g., `IAccounts`, `IRoulette`), unless overridden via `RpcServiceBuilder.HasName()` or `AddLocalApi<T>(..., "CustomName")`.
- **MethodName** — `MethodInfo.Name` (e.g., `GetOwn`, `OnChange`), unless overridden via `[RpcMethod(Name = "...")]`.
- **ParameterCount** — total parameter count including `CancellationToken`.

Examples:

| Interface | Method Signature | Full Name |
|-----------|-----------------|-----------|
| `IAccounts` | `Task<AccountFull> GetOwn(Session, CancellationToken)` | `IAccounts.GetOwn:2` |
| `IChats` | `Task<Chat?> Get(Session, ChatId, CancellationToken)` | `IChats.Get:3` |
| `IChats` | `Task<Chat> OnChange(Chats_Change, CancellationToken)` | `IChats.OnChange:2` |

## Hash Computation Algorithm

The hash is computed in `RpcMethodRef.ComputeHashCode` ([source](https://github.com/ActualLab/Fusion/blob/master/src/ActualLab.Rpc/Configuration/RpcMethodRef.cs)):

1. Encode the full name as UTF-8 bytes.
2. Compute a 4-byte prefix: `(uint)(67211L * utf8Length)`, written as little-endian.
3. Concatenate the prefix and UTF-8 bytes into a single span.
4. Hash with `XxHash3.HashToUInt64(span)`, truncated to `int` (lower 32 bits).

```csharp
using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

static int ComputeMethodHash(string fullName)
{
    var utf8 = Encoding.UTF8.GetBytes(fullName);
    var span = new byte[utf8.Length + 4];
    var prefix = unchecked((uint)(67211L * utf8.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(span, prefix);
    utf8.CopyTo(span.AsSpan(4));
    return unchecked((int)XxHash3.HashToUInt64(span));
}

// Example:
ComputeMethodHash("IAccounts.GetOwn:2") // => 0x0253a0a9 (39035049)
```

## How Hashes Are Used

"mempack5c" and "mempack6c" protocols send method hashes on the wire instead of full names. When a client calls a method:

1. The client computes the hash from the method's full name and sends it.
2. The server looks up the method by hash in `RpcMethodResolver`.
3. If no match is found, the server returns a `NotFound` response.

This is why removing an API interface breaks old clients — the server no longer recognizes the hash codes those clients send.

## Legacy API Compatibility

When a feature is removed but old clients still call its methods, you must:

1. Keep a **legacy interface** (e.g., `ILegacyRoulette`) with stub implementations.
2. Register it with the **original service name** so hashes match:
   ```csharp
   rpcHost.AddLocalApi<ILegacyRoulette, LegacyRoulette>("IRoulette");
   ```
3. Include **all methods** that old clients may call, with the same parameter counts.

The legacy interface methods must have matching full names (same service name + method name + parameter count) so that the computed hashes are identical to what old clients send.

## Legacy Name Overrides

`RpcMethodDef` supports `LegacyNames` for service/method renaming while preserving wire compatibility. These are configured via attributes and allow a method to respond to both its current hash and historical hashes from older protocol versions.
