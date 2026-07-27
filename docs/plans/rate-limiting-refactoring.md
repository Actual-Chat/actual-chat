# Rate Limiting — Refactoring Plan

Status: **steps 1-5 of the sequencing below are implemented** (§0, §2, §3.1-§3.5, §4).
Still open: §5 (budgets against observed traffic), §3.6, §3.7, §7, §8.

Where the plan proved wrong against the code, and what was done instead:

- **`RedisTokenBucketRateLimiter` is not dead code** (open question 4): it limits
  outbound LLM token throughput via `RateLimitedChatCompletionService`, and it needs
  the per-call `permitCount` the plan drops. It was therefore *moved* next to its only
  consumer as `Chat.ML/LlmTokenRateLimiter` instead of being deleted, and stays out of
  the `IRateLimiter` family.
- **`RateLimitClass` keeps `SessionCreation` and `GifProvider`**, which postdate the
  plan: `MobileSessions` and `Gifs` charge them directly. The enum is `HttpRead`,
  `Command`, `Auth`, `SessionCreation`, `GifProvider`.
- **`CallBudget` is not renamed but deleted**: it was identical to
  `SlidingWindowBudget`, so `RateLimitBudgets` holds the latter.
- **Identity resolution can't be synchronous** (§3.3): the user-id dimension needs
  `ISessionsBackend`. `RateLimitIdentityResolver` is therefore async and fills a small
  caller-provided array, which becomes the `ReadOnlySpan<RateLimitIdentity>` the policy
  takes; `ReadOnlySpan<>` can't cross an `await`, and `RateLimitIdentity` holds a string,
  so it can't be `stackalloc`-ed either. The substitutable piece is
  `IRateLimitUserIdResolver` (the renamed `ICallAccountResolver`).
- **The `AccountException` transiency hook was already a no-op** and the new one is too:
  `TransiencyResolvers.PreferTransient` already returns `Transient` for any exception it
  doesn't recognise, and the hook returns early in that case. The classification is
  written down anyway, so a later change can't silently invert it.

The inbound rate limiter added in `cd08b5a6` works but is wrong in design, not
just in tuning. This document states every defect, binds it to source, and
proposes the replacement. Written after review feedback; the criticism is
accepted in full.

---

## 0. The one defect that matters most

**The read path is limited at ~1% of normal traffic, using a mechanism that
cannot go faster.**

`CallBudgets.cs:16` sets Read to `6_000 / 1 min` — **100 calls/s per session**.
In a Fusion app a thousand read calls in a tenth of a second is *ordinary*:
those calls are compute-method calls served from cache, so the natural rate is
thousands per second. The budget is off by roughly two orders of magnitude, and
the comment above it ("~100 calls/s per session: a reconnecting client
resubscribes to every open chat at once") shows the sizing was done as if each
read hit a database.

Worse than the number: **every charged read call makes a Redis round trip.**
`RedisCallLimiter.cs:33-35` builds a limiter and evaluates a Lua script per
identity, sequentially. So the limiter takes calls whose entire point is that
they are cheap and cached, and puts a network hop in front of each one. No
amount of retuning fixes that — a Redis-backed sliding window simply cannot sit
on this path.

**Decision: reads are not rate limited at all.** Not "limited generously" —
removed. Per-session and per-account budgets on commands, plus auth budgets,
plus the transport-level limits, are the protection. Reads are bounded by the
cache, and a client that spins on cached reads is burning its own CPU.

---

## 1. Allocation cost per call, as it stands

Counted for one charged RPC call with three identities. This is the hot path.

| # | Allocation | Source |
|---|---|---|
| 1 | `new List<CallIdentity>(3)` | `RpcCallLimiter.cs:69` |
| 2 | `address.ToString()` — fresh string every call | `CallIdentity.cs:46` |
| 3 | async state machine box (`TryAcquire` is `async Task<>`) | `RpcCallLimiter.cs:58` |
| 4 | `Task<CallLimitResult>` — even `UnlimitedCallLimiter` does `Task.FromResult` | `ICallLimiter.cs:16,32` |
| 5 | interpolated key string, **per identity** | `RedisCallLimiter.cs:33` |
| 6 | `Hash(identity.Value)` result, per identity — and again in the log call | `RedisCallLimiter.cs:33,41` |
| 7 | `new RedisSlidingWindowRateLimiter.Options(...)` — a **record**, heap, per identity | `RedisCallLimiter.cs:34` |
| 8 | `new RedisSlidingWindowRateLimiter(...)` — **a whole limiter object per identity per call** | `RedisCallLimiter.cs:35` |
| 9 | `new RedisKey[]{…}` + `new RedisValue[]{…}` per script call | `RedisTokenBucketRateLimiter.cs:71-76` |
| 10 | `AcquireResult` on the denied path — it is a `record`, i.e. a class | `RedisTokenBucketRateLimiter.cs:96` |
| 11 | `ScriptEvaluateAsync` internals + `(long[])result` cast | `RedisTokenBucketRateLimiter.cs:69,79` |

**~15+ allocations and up to 3 sequential network round trips per call.** The
target for the replacement is **zero allocations on the allowed path**.

Two things that are *not* defects, for the record: `costClass` and
`sessionGetter` are computed once per method in `Create` (`RpcCallLimiter.cs:34-35`),
not per call. That hoisting is correct and should be preserved.

---

## 2. Low-level Redis limiters — `src/dotnet/Redis/`

`RedisTokenBucketRateLimiter` and `RedisSlidingWindowRateLimiter` are near-twins
with no shared contract.

| Defect | Evidence |
|---|---|
| No common interface | both are standalone classes |
| Not `sealed` | `RedisTokenBucketRateLimiter.cs:6`, `RedisSlidingWindowRateLimiter.cs:6` |
| Factory `Create<TContext>` exists only to resolve `RedisDb<TContext>` from DI | `:8` in both — replaced by a plain constructor taking `RedisDb` (§2) |
| `Options` declared at the **bottom** | `RedisTokenBucketRateLimiter.cs:101` — convention is options first |
| **Two separate `AcquireResult` types**, one nested in each class | `RedisTokenBucketRateLimiter.cs:96` and the sliding-window equivalent |
| `AcquireResult` is a `record` → heap allocation per denied call | `:96` |
| `Acquire` / `TryAcquire` / `IsRequestAllowedAsync` — three names, none of which say "check a rate limit" | `:49,55,66` |
| Returns `Task<>` where the local case is synchronous | `:66` |

### Proposed shape

**Where it lives: `ActualChat.Core`, namespace `ActualChat.Resilience`.** Not
`Core.Server`, and not a `Limits` namespace. `Core` so that `Redis`,
`Core.Server` and anything else can implement or consume it; `Resilience` to match
Fusion's own `ActualLab.Resilience`, where `Transiency`, `IHasTimeout` and the new
`IHasRetryDelay` live. Every `ActualChat.Limits.*` namespace goes away.

**Generic in both the key and the budget, with a type-erased escape hatch, and a
nullable budget that falls back to the limiter's default.**

```csharp
namespace ActualChat.Resilience;   // ActualChat.Core

// Erased form: for heterogeneous collections. TBudget : class below is what keeps this box-free.
public interface IRateLimiter<in TKey>
{
    ValueTask<TimeSpan?> Check(TKey key, object? budget, CancellationToken cancellationToken = default);
}

// Typed form: what callers and implementations normally use
public interface IRateLimiter<in TKey, in TBudget> : IRateLimiter<TKey>
    where TBudget : class
{
    ValueTask<TimeSpan?> Check(TKey key, TBudget? budget, CancellationToken cancellationToken = default);
}

public static class RateLimiterExt
{
    // The common case: use the limiter's own default budget
    public static ValueTask<TimeSpan?> Check<TKey>(
        this IRateLimiter<TKey> limiter, TKey key, CancellationToken cancellationToken = default)
        => limiter.Check(key, null, cancellationToken);
}
```

so the everyday call is just:

```csharp
var retryDelay = await limiter.Check(key, cancellationToken);
```

**Note the budget parameter on the interfaces is deliberately *not* optional**, and
the no-budget form is an extension instead. If both interfaces declared
`budget = null` as a default, then `limiter.Check(key)` on a variable of the
**derived** interface would be **ambiguous**: the typed `Check(TKey, TBudget?, …)`
and the inherited erased `Check(TKey, object?, …)` are both applicable with one
supplied argument, and nothing tie-breaks them. Making the parameter required
leaves no applicable instance method for a one-argument call, so the extension wins
cleanly, while `Check(key, someBudget)` still binds to the typed overload because
`TBudget` is more specific than `object`.

One residual sharp edge: `limiter.Check(key, null)` written explicitly *is* still
ambiguous, since `null` converts to both. Callers should use the extension rather
than passing `null` by hand. If that proves annoying in practice, renaming the
erased method to `CheckUntyped` removes the entire class of problem at the cost of
a slightly less uniform name — worth doing if the ambiguity ever bites twice.

`budget: null` means "use the limiter's default", so the common case — one budget
per limiter instance — goes through the extension and passes no budget at all.
Only a call site that genuinely varies the budget supplies one.

Implementations are plain constructors; there is **no factory method**:

```csharp
public sealed class RedisSlidingWindowRateLimiter(
    RedisDb redisDb,
    string keyPrefix,
    SlidingWindowBudget defaultBudget
) : IRateLimiter<string, SlidingWindowBudget>
{
    private readonly RedisDb _redisDb = redisDb.WithKeyPrefix(keyPrefix);

    public ValueTask<TimeSpan?> Check(
        string key, SlidingWindowBudget? budget, CancellationToken cancellationToken = default)
        => CheckImpl(key, budget ?? defaultBudget, cancellationToken);

    ValueTask<TimeSpan?> IRateLimiter<string>.Check(string key, object? budget, CancellationToken ct)
        => Check(key, (SlidingWindowBudget?)budget, ct);
}

public sealed record SlidingWindowBudget(int Limit, TimeSpan Window);
// TokenBucketBudget is not needed: RedisTokenBucketRateLimiter is deleted (open question 4)
```

Constructed at the call site, plainly:

```csharp
var limiter = new RedisSlidingWindowRateLimiter(redisDb, "auth", new SlidingWindowBudget(30, 5.Minutes()));
var retryDelay = await limiter.Check(key, cancellationToken);   // default budget, via the extension
```

Why this shape:

- **The prefix folds into the `RedisDb`, once.** `RedisDb.WithKeyPrefix`
  (`ActualLab.Redis/RedisDb.cs:37-40`) composes through `FullKey`, so the limiter
  keeps a pre-prefixed `RedisDb` and per-call keys need no concatenation. That
  removes the interpolated key string that `RedisCallLimiter.cs:33` builds per
  identity per call.
- **No `New<TContext>` factory.** The existing `Create<TContext>` factories
  (`RedisTokenBucketRateLimiter.cs:8`) exist only to resolve
  `RedisDb<TContext>` from the container; a constructor taking `RedisDb` lets DI do
  that, and the limiter stops depending on `IServiceProvider` at all.
- **`where TBudget : class` is not cosmetic.** The erased overload takes `object?`,
  so a struct budget would be **boxed on every check** — an allocation on exactly
  the path this refactor exists to clear. Reference-type budgets make the erased
  path allocation-free, and cost nothing real: budgets are configuration records
  built once.
- **Safe by default, erased on request.** Normal call sites use the two-parameter
  interface and get a compile error on a mismatched budget. The one-parameter form
  exists only where a heterogeneous set of limiters must be held together, and the
  cast is confined to one forwarding method.
- **The key stays generic.** A local in-process limiter can use
  `Dictionary<TKey, …>` with a **struct** key and never format a string — no
  interpolation, no hashing an interpolated string, nothing allocated. The Redis
  implementations take `string` because that is what the wire needs.
- **Construction state vs per-check budget.** The `RedisDb`, prefix and default
  budget are construction state; a budget override is a per-check argument. Today
  all of it — including the key — is one `Options` record
  (`RedisTokenBucketRateLimiter.cs:101`), which is exactly why a limiter has to be
  constructed per key on every call (`RedisCallLimiter.cs:34-35`).

An earlier draft proposed a single non-generic `IRateLimiter` taking an abstract
`RateLimitOptions` base type that every implementation would cast. This is
strictly better: the typed form makes a mismatch a compile error, and where
erasure is genuinely needed it is explicit and box-free rather than the default.

- **Limiters are long-lived singletons.** There is a dictionary lookup on the key
  either way, so an ephemeral limiter buys nothing and costs an allocation.
- `permitCount` is **dropped**: token-bucket-only
  (`RedisTokenBucketRateLimiter.cs:23,38-39`), absent from the sliding window
  (`RedisSlidingWindowRateLimiter.cs:48-49`), never passed by anything. If a
  per-method cost model is wanted later it belongs in the budget of whichever
  limiter can honour it, not in the shared contract.
- Both nested `AcquireResult` records are **deleted**, not merged; the result is
  `TimeSpan?`.
- Both classes `sealed`.
- `Acquire` (`:55-64`) and `IsRequestAllowedAsync` (`:49`) are deleted: a blocking
  retry loop inside a limiter is a foot-gun, and the boolean form is
  `Check(...) is null`.

### How the policy holds differently-typed limiters

`IRateLimiter<TKey>` is what makes this straightforward: the policy holds limiters
in their erased form, so a rule only has to be generic in the key.

```csharp
// Core.Server — one instance per configured limit, built at startup
public abstract class RateLimitRule
{
    public abstract ValueTask<TimeSpan?> Check(in RateLimitIdentity identity, CancellationToken ct);
}

public sealed class RateLimitRule<TKey>(
    IRateLimiter<TKey> limiter,
    object? budget,                      // null = the limiter's own default
    Func<RateLimitIdentity, TKey> keyBuilder
) : RateLimitRule
{
    public override ValueTask<TimeSpan?> Check(in RateLimitIdentity identity, CancellationToken ct)
        => limiter.Check(keyBuilder.Invoke(identity), budget, ct);
}
```

The policy holds `RateLimitRule[]`; each check is one virtual call plus the
limiter's own internal cast. If every server-side key ends up being a `string` —
which is likely, since the Redis-backed limiters need one — `RateLimitRule` can
drop its type parameter entirely and the erasure disappears from the policy
altogether.

Rules, budgets and key-builder delegates are all constructed once at startup, so
nothing here allocates per call. The pairing of limiter to budget is still worth
verifying at construction: build each rule through a small typed helper that takes
`IRateLimiter<TKey, TBudget>` and `TBudget`, so the compiler checks the pair before
it is erased into the rule.

### What replaces the budget layer: `RateLimitPolicy`, in `Core.Server`

`RateLimitClass`, `RateLimitBudgets` and the identity machinery are **not** part of
the limiter contract. They belong to a policy object that lives in
`ActualChat.Core.Server` — server-side, since it depends on sessions, accounts and
`HttpContext`:

- holds the configured set of rate limits as `RateLimitRule[]` — each pairing a
  limiter, its budget and a key builder, type-checked at construction;
- resolves per-call identities (§3.3), which the rules turn into keys;
- **translates that into a set of `Check` calls**, sequential for the local path
  and concurrent for the Redis-backed auth path (§3.5);
- logs which dimension tripped and throws `RateLimitExceededException` (§4).

Name: **`RateLimitPolicy`**. `CombinedRateLimiter` and `MultiRateLimiter` were
considered and rejected — both imply it *is* a limiter, the same category error as
naming a middleware `HttpCallLimiter`.

---

## 3. The policy layer — today `src/dotnet/Core.Server/Limits/`

### 3.1 Naming

Every name says "call" where it means "rate limit", and two types named
`…CallLimiter` are not limiters at all — they are middleware that *consume* an
`ICallLimiter`.

| Current | Proposed | Why |
|---|---|---|
| `ICallLimiter` | **deleted** -- folded into `IRateLimiter` (§2) | multi-limit is the same contract |
| `CallLimitResult` | **deleted** -- `ValueTask<TimeSpan?>` + exception | nothing branched on it (§3.2) |
| — | new `IRateLimiter<TKey, TBudget>` + per-algorithm budget records (§2) | limit parameters are algorithm-specific; generics keep it compile-time safe |
| `CallBudget` / `CallBudgets` + identity mapping | `RateLimitPolicy` | it is policy, not a limiter |
| `CallIdentity` | `RateLimitIdentity` | says what it is |
| `CallIdentityKind` | `RateLimitIdentityKind` | ditto |
| `CallCostClass` / `CallCostClasses` | `RateLimitClass` / `RateLimitClassResolver` | "cost class" implied a cost model that doesn't exist |
| `CallCostClass.Read` | `RateLimitClass.HttpRead` | HTTP-only by name, so no RPC path can charge it |
| `CallIdentityKind.Account` | `RateLimitIdentityKind.UserId` | it is a user id, not an account object |
| `CallIdentityKind.IPAddress` | `RateLimitIdentityKind.IP` | `IP` is the phrasing used throughout |
| `CallIdentity.ForAddress` | `RateLimitIdentity.ForIP` | consistent with the above |
| `ICallAccountResolver` (account only) | resolves a **user id** dimension | see §3.3 |
| `CallBudget` / `CallBudgets` | `RateLimitBudget` / `RateLimitBudgets` | consistent |
| `ICallAccountResolver` | `IRateLimitIdentityResolver` | see §3.3 |
| namespace `ActualChat.Limits` | `ActualChat.Resilience`, in `ActualChat.Core` | matches `ActualLab.Resilience`; usable from `Redis` |
| `RpcCallLimiter` | `RpcRateLimitMiddleware` | it is an `IRpcMiddleware` |
| `HttpCallLimiter` | `HttpRateLimitMiddleware` | matches `McpAuthMiddleware` precedent |
| `UnlimitedCallLimiter` | `UnlimitedRateLimitChecker` | consistent |
| `RedisCallLimiter` | `RedisRateLimitChecker` | consistent |

### 3.2 Where the result went, and who throws

Every consumer of `CallLimitResult` either throws or maps to `429`:

| Site | What it does with the result |
|---|---|
| `RpcCallLimiter.cs:39-40` | `if (!IsAllowed) throw result.ToError()` |
| `CallLimiterExt.cs:16-17` (`Require`) | `if (!IsAllowed) throw` |
| `EmailAuth.cs:171` | calls `Require` -> throw |
| `HttpCallLimiter.cs:35-39` | reads `RetryAfter` for the `Retry-After` header |

No branch needs a result value -- only the retry hint, which the exception
carries. So **`CallLimitResult` is deleted**, and with it `CallLimiterExt.Require`
(checking *is* requiring).

That leaves two layers with deliberately different contracts:

| Layer | Contract | Why |
|---|---|---|
| `IRateLimiter<TKey, TBudget>` (§2, in `ActualChat.Core`) | `ValueTask<TimeSpan?> Check(TKey key, TBudget budget, …)` | a primitive: returns the delay, throws nothing, so it stays reusable and composable |
| `RateLimitPolicy` (beside the middlewares) | `ValueTask Check(string method, RateLimitClass, ReadOnlySpan<RateLimitIdentity>, …)` -- throws | knows the budgets, builds the keys, logs which dimension tripped, then throws |

The primitive returning `TimeSpan?` rather than throwing matters: it is the layer
a future local-first implementation (§7) and any non-request caller reuse, and an
exception is the wrong control-flow tool for something the caller may legitimately
want to branch on. The policy layer above it is where "denied" becomes an
exception, because by then there is exactly one correct reaction.

- `ReadOnlySpan<RateLimitIdentity>` in the policy, replacing
  `IReadOnlyList<CallIdentity>` (`ICallLimiter.cs:19`) -- the caller
  stack-allocates and no list is built. The limiter itself takes one key.
- `ValueTask` in both -- a local check is synchronous and must not allocate a
  `Task` per call.
- `HttpRateLimitMiddleware` catches `RateLimitExceededException` and maps it to
  `429` + `Retry-After` from `e.RetryDelay`, instead of inspecting a struct.

**`ExhaustedKind` is not carried anywhere.** It is currently written at
`RedisCallLimiter.cs:43` and **read by nobody**; the identity kind is already
logged server-side at `:38-42`, which is where it belongs. Putting it on the
exception would leak which dimension tripped -- session vs account vs IP vs
target -- to the client, since Fusion serialises RPC errors. Server-side log only.

**Caveat to resolve during implementation:** a `ReadOnlySpan<>` parameter cannot
cross an `await` in the same method, so the async Redis path must copy the span
into a small fixed buffer (or re-derive it) before awaiting. Fine for the auth
path, but it must not force an allocation on the local path. If it proves
awkward, the fallback is a synchronous `TryCheck` for the local case plus a
separate async entry point for Redis -- still better than allocating per call.

### 3.3 Identity resolution

`ICallAccountResolver` (`ICallAccountResolver.cs:7`) resolves only the account,
only from a `Session`, and the two middlewares each hand-roll the rest —
`RpcCallLimiter.cs:69-81` builds the list inline, `HttpCallLimiter` does its own
variant.

Replace with one resolver that takes a source and fills a span:

```csharp
public interface IRateLimitIdentityResolver
{
    int Resolve(in RateLimitSource source, Span<RateLimitIdentity> buffer);
}
```

where `RateLimitSource` wraps whichever of `HttpContext` / `RpcConnection` /
`Session` is available. One implementation, two call sites, no duplicated
list-building. `Target` stays caller-supplied (it is the only identity the
framework cannot derive — see `EmailAuth.cs:164`).

Also: `address.ToString()` per call (`CallIdentity.cs:46`) should be avoided —
either cache the formatted string on the connection next to the snapshotted
`IPAddress`, or key on the address bytes.

### 3.4 Which classes are actually limited

| Class | Transport | Mechanism |
|---|---|---|
| **Read** | RPC | **not limited at all** |
| **Read** | HTTP | keep, generously — controllers are not Fusion compute calls |
| **Command** | both | **local, in-process limiter** |
| **Auth** | both | Redis (must be cross-node authoritative) |

The Redis dependency is thereby confined to auth, where volumes are tiny
(`30 / 5 min`) and correctness across nodes genuinely matters. Commands go
local: a per-node limit is the right trade because command volume per session is
low and a node-local bound is enough to stop a runaway client.

This also retires a caveat noted when the IP budgets were enabled:
`RedisSlidingWindowRateLimiter` stores one ZSET member per permitted call, so a
600,000/min Read budget could pin 40–60 MB of Redis on one key. With reads
unlimited and commands local, that disappears.

### 3.5 Enforcing several limits at once

With a single-key `Check` (§2), enforcing N limits is **N calls** made by
`RateLimitPolicy`. Per path:

- **Read** — no checks at all (§0). Question does not arise.
- **Command** — local in-process limiter. Each check is nanoseconds; running them
  concurrently would cost more than the work and allocate a task apiece.
  **Sequential.**
- **Auth** — Redis, up to four identities, so sequential means up to **four round
  trips**. Latency is real here, and since auth is rare the task allocations do
  not matter. **Run them concurrently.**

**Every dimension is charged, including on a rejected call — this is intended.**
An earlier draft treated it as a defect and argued for all-or-nothing charging via
a single batched script. That was wrong. The counters measure **calls made, not
calls served**:

- The call reached the server and consumed real work — identity resolution, the
  checks themselves, a round trip apiece — whichever dimension ultimately refused
  it.
- If a rejection exempted the other dimensions, tripping a *cheap* dimension
  deliberately (a caller's own session budget, say) would become a way to make
  calls that never accrue cost against the IP or account dimensions. Rejection
  would buy free probes.
- A caller who is being limited is, by definition, making too many calls. Counting
  those calls everywhere is the honest measurement.

So the N-independent-calls shape is not a compromise forced by the single-key
interface — it is the semantics we want, and the interface happens to match it.
No batched script, and no optional `IBatchRateLimiter`: both would exist purely to
implement behaviour we have now decided against.

### 3.6 Composite identities, and why "exhaust IP first" is rejected

One option considered and **rejected**: an ordering rule where the IP budget is
exhausted first and only then the account budget is consulted. It gives no
useful property — the dimensions measure different things, so consuming one
before the other is arbitrary, and it makes the observed limit depend on call
order.

What is useful instead is a **composite identity**: `UserIdAndIP` as a single
key, so that two users behind one NAT are tracked separately rather than
sharing a bucket. Alongside it you still want a **separate, higher, IP-only
limit** — the composite catches "one user misbehaving from one place", the
IP-only catches "one place misbehaving across many users".

So `RateLimitIdentity` must permit composite values, e.g.
`RateLimitIdentityKind.UserIdAndIP` with a value combining both. That is
still just another entry in the identity span, so it batches with the rest
(§3.5) — no new mechanism, one more dimension. Key construction must stay
allocation-lean: build the composite value once when the identity is resolved,
not per check.

### 3.7 Connection and session cardinality — a different shape entirely

Two limits worth adding that are **not rate limits**:

- **connections per session id** — how many live RPC connections one session holds
- **sessions per user** — how many sessions one user id has

These are *gauges*, not counters: they go up and down, so they need
increment-on-connect and decrement-on-disconnect with cleanup for connections
that die without notice. A sliding-window or token-bucket limiter cannot express
them, and forcing them through `IRateLimiter` would be the same category
error as naming a middleware a limiter.

Notes on each:

- **Sessions per user** is already queryable — `DbSession` rows carry the
  account — so it is most naturally enforced **at session creation**, in the same
  place `MobileSessions` creates them, rather than through a limiter at all.
- **Connections per session** needs live connection state. The RPC layer already
  tracks peers and connections (`RpcPeer.ConnectionState`), so the count exists;
  what is missing is a check at handshake, in `GetServerConnection` where the
  session is already resolved (`RpcBackendHelpers.cs:88-100`).

Both are out of scope for this refactor. Recorded here so they are not
accidentally implemented as rate limits.

---

## 4. The exception

`CallLimiterExt.cs:5-6` throws `StandardError.Constraint(...)`, which is an
`InvalidOperationException` — indistinguishable from a dozen unrelated failures,
when rate limiting is precisely the condition a caller wants to recognise, back
off from, or surface as `429`.

Since there is no result type (§3.2), the exception *is* the channel:

```csharp
public sealed class RateLimitExceededException : Exception
{
    public RateLimitExceededException() : this(null) { }
    public RateLimitExceededException(string? message) : base(message ?? "Too many requests.") { }

    public TimeSpan RetryAfter { get; init; }   // in-process only — see below
}
```

`RetryAfter` only. **No identity kind, no budget, no limit value** — those
describe how we enforce limits internally and must not reach a client. They are
logged server-side instead.

### It must not be re-wrapped, and that dictates where it lives

Fusion transmits RPC errors as `ExceptionInfo`, which carries **exactly two
fields** — `TypeRef` and `Message` (`ActualLab.Core/Serialization/ExceptionInfo.cs:27-30`)
— and rebuilds the exception client-side via `ToException()` (`:57`) by resolving
`TypeRef`. Two consequences:

1. **The type must be declared in a client-visible assembly.** Put it in
   `ActualChat.Core` next to `AccountException`
   (`Core/Errors/StandardError.Account.cs:21`), **not** in `Core.Server`. If the
   client cannot resolve the `TypeRef`, reconstruction falls back to
   `ExceptionInfo.UnknownExceptionTypeResolver` (`:25`) and the caller sees some
   other type — which is exactly the re-wrapping we are trying to avoid. It also
   needs a public `(string? message)` constructor for `ToException()` to invoke.
2. **`RetryAfter` does not survive an RPC hop.** Only the message crosses the
   wire, so on the client the property is `default`. This is a real limitation,
   not an oversight — spelling it out:
   - `HttpRateLimitMiddleware` catches the exception **in-process**, so the
     property is intact there and the `Retry-After` header is accurate.
   - An RPC client learns the *type* (which is the part that matters for
     recognising and not auto-retrying) plus a human-readable hint in the
     message.
   - If a machine-readable retry hint is ever needed client-side, the options are
     encoding it in the message and parsing it, or extending `ExceptionInfo`
     upstream in Fusion. Do neither now.

Nothing in our code should catch and rethrow it as something else. It should
propagate untouched from the checker to the transport boundary.

### Transiency: transient, and Fusion must honor the delay

An earlier draft of this document said **non-transient**. That was wrong, and the
reason is worth recording because it is not obvious.

A rate-limit error is inherently temporary -- the value *will* become available --
so it is `Transiency.Transient`. Classifying it non-transient sends it down
`Computed.cs:422`, `Min(AutoInvalidationDelay, NonTransientErrorInvalidationDelay)`
= 30 s by default: an arbitrary horizon unrelated to the actual retry delay, too
long for a 4-second limit and far too short for a 5-minute one.

But **transient alone is worse.** `Computed.cs:420` maps transient to
`Options.TransientErrorInvalidationDelay`, whose default is **1 s**
(`ComputedOptions.cs:28-29`). The cached error is invalidated after a second, the
client immediately re-requests, and *the retry itself charges the limiter again*.
A client stuck behind a limit would generate more load than one that is not --
a positive feedback loop, the exact opposite of what a limiter is for.

**Resolved upstream.** `IHasRetryDelay` was added to `ActualLab.Resilience`, and
`Computed.StartAutoInvalidation` now extends the error-invalidation timeout to at
least the error's own `RetryDelay` (Fusion `5d1522a9c`, branch
`feature/retry-delay-aware-errors`, pushed). So: classify **transient**, implement
`IHasRetryDelay`, and the cached error is not invalidated -- and therefore not
retried -- before the delay elapses.

**Pending:** `IHasRetryDelay` is not implemented yet -- it isn't part of the
`ActualLab` package version this repo references (14.1.47), so the exception only
carries and parses `RetryDelay` for now. Add the interface once the package is bumped.

### The delay must round-trip through the message

`ExceptionInfo` carries only `TypeRef` and `Message`, so `RetryDelay` cannot ride
as a property. It has to be **encoded in the message and parsed back** by the
constructor `ExceptionInfo` uses.

`TryCreateException` (`ExceptionInfo.cs:94-119`) tries
`(string message, Exception? innerException)` **first**, then a single-parameter
constructor whose parameter is **named exactly `message`**. So the two-argument
constructor is where parsing must happen:

```csharp
public sealed class RateLimitExceededException : Exception, IHasRetryDelay
{
    public TimeSpan RetryDelay { get; }

    public RateLimitExceededException(TimeSpan retryDelay)
        : base(FormatMessage(retryDelay))
        => RetryDelay = retryDelay;

    // The ctor ExceptionInfo.ToException() uses -- must recover RetryDelay from the message
    public RateLimitExceededException(string? message, Exception? innerException = null)
        : base(message, innerException)
        => RetryDelay = ParseRetryDelay(message);
}
```

The message format is **`"Too many requests. Retry in 5s."`** — a normal
sentence, not a machine token. That is what a user or a log reader sees, so it has
to read properly; the parser has to work with it rather than the other way round.

```csharp
private static string FormatMessage(TimeSpan retryDelay)
    => $"Too many requests. Retry in {ToSeconds(retryDelay)}s.";

// Anchored at the end so a caller-supplied prefix can't confuse it
private static readonly Regex RetryDelayRe = new(@"Retry in (\d+)s\.$", RegexOptions.Compiled);
```

Requirements on the encoding:

- **Whole seconds, rounded up, minimum 1.** Rounding up is the safe direction: a
  round-tripped delay may be up to a second longer than the original but never
  shorter, so a client can never be told to retry earlier than the server meant.
  It also matches HTTP `Retry-After`, which is second-granularity anyway, and it
  removes the sub-second oracle into the window's internal state that an exact
  value would give.
- **Integer + `s`, so there is no pluralisation problem.** `"Retry in 1s."` and
  `"Retry in 5s."` are both correct English; spelling out "1 second" / "2 seconds"
  would need pluralisation logic for no gain.
- **Culture-invariant.** An integer and a literal `s`, formatted invariantly — no
  decimal separator to vary by locale.
- **Anchored parse.** The regex matches at the *end* of the message, so a caller
  that prefixes context does not break recovery.
- **Parsing must never throw.** No match, or a number that does not fit, yields
  `TimeSpan.Zero`; zero means "no delay specified", and Fusion's wiring already
  guards on `RetryDelay.Ticks: > 0`.
- **Round-trip test is mandatory**, and it must assert the *ceilinged* value:
  `new RateLimitExceededException(TimeSpan.FromSeconds(4.2))` → `ExceptionInfo` →
  `ToException()` → a `RateLimitExceededException` whose `RetryDelay` is 5s. Also
  cover a message with no token (→ `Zero`) and a sub-second delay (→ 1s).

Nothing in our code should catch and rethrow it as something else. It should
propagate untouched from the checker to the transport boundary.

### Transiency: transient, and Fusion must honor the delay

An earlier draft of this document said **non-transient**. That was wrong, and the
reason is worth recording because it is not obvious.

A rate-limit error is inherently temporary -- the value *will* become available --
so it is `Transiency.Transient`. Classifying it non-transient sends it down
`Computed.cs:422`, `Min(AutoInvalidationDelay, NonTransientErrorInvalidationDelay)`
= 30 s by default: an arbitrary horizon unrelated to the actual retry delay, too
long for a 4-second limit and far too short for a 5-minute one.

But **transient alone is worse.** `Computed.cs:420` maps transient to
`Options.TransientErrorInvalidationDelay`, whose default is **1 s**
(`ComputedOptions.cs:28-29`). The cached error is invalidated after a second, the
client immediately re-requests, and *the retry itself charges the limiter again*.
A client stuck behind a limit would generate more load than one that is not --
a positive feedback loop, the exact opposite of what a limiter is for.

**Resolved upstream.** `IHasRetryDelay` was added to `ActualLab.Resilience`, and
`Computed.StartAutoInvalidation` now extends the error-invalidation timeout to at
least the error's own `RetryDelay` (Fusion `5d1522a9c`, branch
`feature/retry-delay-aware-errors`, pushed). So: classify **transient**, implement
`IHasRetryDelay`, and the cached error is not invalidated -- and therefore not
retried -- before the delay elapses.

### The delay must round-trip through the message

`ExceptionInfo` carries only `TypeRef` and `Message`, so `RetryDelay` cannot ride
as a property. It has to be **encoded in the message and parsed back** by the
constructor `ExceptionInfo` uses.

`TryCreateException` (`ExceptionInfo.cs:94-119`) tries
`(string message, Exception? innerException)` **first**, then a single-parameter
constructor whose parameter is **named exactly `message`**. So the two-argument
constructor is where parsing must happen:

```csharp
public sealed class RateLimitExceededException : Exception, IHasRetryDelay
{
    public TimeSpan RetryDelay { get; }

    public RateLimitExceededException(TimeSpan retryDelay)
        : base(FormatMessage(retryDelay))
        => RetryDelay = retryDelay;

    // The ctor ExceptionInfo.ToException() uses -- must recover RetryDelay from the message
    public RateLimitExceededException(string? message, Exception? innerException = null)
        : base(message, innerException)
        => RetryDelay = ParseRetryDelay(message);
}
```

Requirements on the encoding:

- **Machine-readable and stable** -- a fixed token such as a trailing
  `[retryAfter=4200ms]`, not prose like "retry in 4.2s", which breaks the moment
  anyone rewords or localises the sentence.
- **Culture-invariant**, integer milliseconds.
- **Parsing must never throw**: a missing or malformed token yields
  `TimeSpan.Zero`, and zero means "no delay specified" -- Fusion's wiring already
  guards on `RetryDelay.Ticks: > 0`.
- **A round-trip test is mandatory**: `new RateLimitExceededException(delay)` ->
  `ExceptionInfo` -> `ToException()` -> same type, same delay.

Nothing in our code should catch and rethrow it as something else: it must
propagate untouched from the limiter to the transport boundary, which is the other
reason the type belongs in `ActualChat.Core`.

---

## 5. Budgets, restated

Reads unlimited over RPC. Remaining numbers to be set with the read path gone,
and to be sanity-checked against real traffic rather than derived from first
principles as the current ones were. Starting proposal:

| Class | Identity | Budget | Reasoning |
|---|---|---|---|
| Command | Session | 600 / 1 min | 10/s sustained; far above human-driven command rate, still bounds a runaway client |
| Command | UserId | 1_200 / 1 min | 2 concurrent devices at the session limit |
| Auth | Session | 30 / 5 min | ~4 calls per sign-in attempt, room for resends |
| Auth | UserId | 30 / 5 min | as above |
| Auth | IP | 600 / 5 min | shared NAT/CGNAT egress must not lock out an office |
| Auth | Target | 30 / 5 min | per phone/email — the tightest bound, since one code gets few attempts |

Read/IP and Write/IP entries (`CallBudgets.cs:21-22`) are deleted
along with the read path.

---

## 6. Open questions for review

1. **Answered:** `IRateLimiter<TKey, TBudget>` and the budget records live in
   `ActualChat.Core`, namespace `ActualChat.Resilience`; `RateLimitPolicy` and
   `RateLimitRule` live in `ActualChat.Core.Server` (§2). Left as a record of the
   decision.
2. **Answered: keep it, renamed `HttpRead`.** The name makes the restriction
   structural rather than a runtime guard the RPC middleware could forget: there
   is no `Read` member for an RPC path to charge. The enum becomes
   `HttpRead`, `Command`, `Auth`.
3. **Answered: yes** — a node-local command limiter is acceptable. The trade is
   that a client spread across N nodes gets up to N× the budget; bounded and
   accepted. Same decision as question 7.
4. **Answered: delete `RedisTokenBucketRateLimiter`.** The sliding window is the
   one with a caller (`RedisCallLimiter.cs:35`); with `permitCount` dropped the
   bucket has no distinguishing feature left, and one algorithm is simpler to
   maintain than two behind the same interface.
5. **Answered: charging every dimension on a rejected call is *desired*** (§3.5)
   — the counters measure calls made, not calls served, and exempting dimensions
   on rejection would let a caller buy free probes by tripping a cheap one. No
   batched script, no `IBatchRateLimiter`.
6. **Answered: one kind with a combined value**, `UserIdAndIP` — not a general
   N-part identity. Cheaper, and it covers the case we actually have (§3.6).
7. **Answered: yes, approximate is acceptable for commands** — bounded overshoot
   in exchange for no network on the hot path. §7 is therefore the agreed
   destination, not a speculative option. Auth stays `Authoritative`.
8. **Answered:** two interfaces in one family —
   `IRateLimiter<TKey>` (erased) and `IRateLimiter<TKey, TBudget>` (typed) —
   plus `RateLimitPolicy`, a concrete helper that throws rather than an
   interface. Multi-limit is N `Check` calls made by the policy (§3.5), not a
   batched argument. Left as a record of the decision.

---

## 7. Where this should end up: local-first counters, periodic Redis reconciliation

**Approved in principle** (open question 7): approximate limiting with bounded
overshoot is acceptable for commands. This is the agreed destination for the
command path, not a speculative option — it is later in the sequence only
because the steps before it are smaller and independently useful.

The design above still consults Redis on the auth path for every call. That is
acceptable because auth volume is tiny, but it is not the right long-term shape
for anything higher-volume. The target design:

**Keep a local counter per key; reconcile with Redis rarely.**

Per node, per key, hold three values: the last global total pulled from Redis
(`globalSnapshot`), the local increments since that pull (`localDelta`), and the
time of the last sync. The effective count is `globalSnapshot + localDelta`, and
a check compares that against the limit and bumps `localDelta` — **no network,
no allocation.**

Reconcile when any of these trips:
- more than ~1–2 seconds have passed since the last sync, or
- `localDelta` exceeds a share of the limit (so a burst syncs immediately), or
- the effective count crosses a high-water mark, e.g. 80% of the limit — near
  the boundary precision starts to matter, far from it nobody cares.

A reconcile is a **single round trip**: push the delta and read back the new
global total (`INCRBY key delta` returns it), then zero `localDelta` and store
the result as the new `globalSnapshot`.

Net effect: normal traffic is served entirely from memory, while genuine abuse
crosses the threshold quickly and becomes globally visible. If someone tortures
the system, we still see it.

### What this forces to change

- **The window algorithm.** A delta cannot be pushed into a ZSET of per-call
  timestamps, so the sliding window has to give way to a **bucketed counter** —
  `INCRBY` on a key whose TTL is the window. That is a small precision loss
  (fixed windows admit a 2× burst across a boundary; two half-buckets mitigate
  it) for a large gain, and it independently fixes the Redis memory issue: one
  integer per key instead of one ZSET member per permitted call.
- **Overshoot becomes explicit.** With N nodes each allowing up to the sync
  threshold before reconciling, the global limit can be exceeded by up to
  `N × threshold`. That is bounded and tunable, and it must be stated in the API
  docs rather than discovered — the limiter becomes *approximate by design*.
- **Per-class enforcement policy**, replacing "everything goes to Redis":

  | Policy | Meaning | Use for |
  |---|---|---|
  | `Local` | node-local only, never synced | commands, per-session keys |
  | `DeltaSynced` | local counter, periodic reconcile | user-id and IP keys, high volume |
  | `Authoritative` | Redis on every check | auth and target keys, where overshoot is unacceptable |

## 8. Session affinity would make most of this unnecessary

Users are not currently pinned to a node. They could be, using the **Google Cloud
load balancer's session-affinity cookie** — an easy change we have not made.

If sessions are pinned, every session-keyed limit becomes single-node, so N = 1
and `Local` counters are not merely approximate, they are **exact** for that
dimension. Only user-id and IP keys — which legitimately span nodes, via multiple
devices and NAT — would still need reconciliation, and auth would still want
`Authoritative`.

Affinity has independent benefits beyond rate limiting (Fusion cache locality,
fewer cross-node invalidations), so it is worth pursuing on its own merits. It is
**not a prerequisite** for §8: delta reconciliation works without it, just with a
larger overshoot bound. But the two compose, and if affinity lands first, §7 gets
both cheaper and more accurate.

---

## 9. Sequencing

**Now — fixes a live misconfiguration:**

1. §3.4 — remove the RPC read path. Deletes both the 100×-too-low budget and the
   per-read Redis hop. Standalone; land it first even if everything else waits.
2. §4 — dedicated exception in `ActualChat.Core`, transiency classification.
   Small, and it unblocks callers being able to recognise the condition.

**Then — cleanup, no behaviour change:**

3. §2 — low-level limiters: `IRateLimiter<TKey, TBudget>` with a nullable budget,
   `TimeSpan?` result, plain constructors taking `RedisDb` + prefix + default
   budget, `sealed`, `permitCount` dropped, `AcquireResult` deleted.
4. §3.1 — renames, mechanical.
5. §3.2/§3.3 — span signature, no result type, unified identity resolver.

**Then — the real design:**

6. §3.5 — batch the auth check into one script with all-or-nothing charging.
7. §3.4 — local in-process limiter for commands.
8. §5 — budgets, set against observed traffic rather than arithmetic.

**Later, tracked but not scheduled:**

9. §7 — local-first counters with periodic Redis reconciliation, and the switch
   from sliding window to bucketed counters that it requires. **Agreed in
   principle**; scheduled after the steps above because they are smaller and
   independently useful.
10. §3.6 — composite `account + IP` identity.
11. §3.7 — connection-per-session and session-per-user cardinality limits,
    which are gauges and do not belong in this API at all.
12. §8 — session affinity via the GCLB cookie. Independent value; makes step 9
    both cheaper and exact for session-keyed limits.

Steps 1–2 are worth doing regardless of whether the rest is approved. Steps 3–5
are safe to do mechanically. Steps 6–8 are where judgement is needed. Steps 9–12
are a separate piece of work with its own review.
