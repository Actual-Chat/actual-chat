# Live location sharing — per device, newest device wins

Issue: [#4177](https://github.com/Actual-Chat/actual-chat/issues/4177)

## TL;DR

A live share is owned by **the device that started it**, and there is still at
most **one live share per author per chat** — but the rule that keeps it that
way flips: today the *first* device wins and the second **adopts its row**;
after this change the *newest* device wins and **takes the share over**.

Adoption is the bug. Two devices holding one row overwrite each other's
position every 30s (phone GPS vs. laptop IP fix — the marker teleports), and
whichever stops first freezes the row while the other keeps reporting into it,
showing "sharing" with a countdown while nothing is shared.

A takeover **freezes** the old row (`StoppedAt = now`) and mints a new one, and
the server already ignores every write to a frozen row. That single fact is
what makes this safe for clients that can't be updated: an old app keeps
pushing into a row that no longer accepts anything, and nothing it pushes
reaches the chat. **No client is forced to upgrade.** New clients additionally
support *remote stop* — noticing that their share was taken or stopped
elsewhere and shutting their tracker down, and stopping another device's share.

The change, in five parts:

1. **Server: takeover on create.** A `Create` with `LiveDuration > 0` freezes
   the author's live share in that chat and mints a new row, in one transaction
   under the existing per-author advisory lock. A new row, never a reused one —
   the old device knows the old id and would keep writing to it.
2. **Server: ignore cheaply.** Writes to a frozen row are already no-ops, but
   they cost an operation transaction and an advisory lock apiece — every 30s,
   forever, per stuck device. They get rejected before that cost is paid.
3. **Client: remote stop.** The loser stops itself through three layered
   mechanisms covering active, backgrounded, offline and killed devices — see
   [the state matrix](#how-the-losing-device-finds-out). All are *fail-open*: a
   share is dropped only when the server definitely says it isn't live.
4. **Client: stop from any device.** Authorization is already author-level, so
   the stop button resolves the author's live share and removes it by id.
5. **Server: make an old client's Stop work.** An old device stops by the id it
   holds, which the takeover froze — so its Stop button quietly stops nothing.
   The RPC handshake tells the server that caller is old, and for those callers
   a `Remove` of an already-frozen share is read as "stop my live share here".

There is no setting to turn any of this on or off: takeover is the rule, and
both old and new clients are supported under it.

Because at most one live share per author per chat still holds, none of the
map/marker/participant UI needs deduping. **No migration.**

## Goal

Starting a live share on a second device moves the sharing to it: the first
device stops tracking, drops its foreground service, and says so; the chat keeps
showing exactly one live location for that person. Devices that are too old to
notice keep working, keep being ignored, and are never made worse than they are
today.

## Why

Sharing is per device on the client and per author on the server, and the two
models collide.

`LiveLocationReporter` keeps a device-local `ActiveShare[]` in `LocalSettings`,
at most one entry per chat, and reports into the `SharedLocationId` it
persisted. But `SharedLocationsBackend.OnChange` refuses to mint a second live
share for an author and hands back the running one instead:

```csharp
// One live share per author: hand back the running one instead of starting a second.
var live = await GetOwnLiveShare(dbContext, authorId, now, cancellationToken).ConfigureAwait(false);
if (live is not null)
    return live;
```

So the second device adopts the first device's row, and both then own it:

1. **The position ping-pongs.** Both devices `Change.Upsert` into the same row
   every `Constants.Location.UpdatePeriod`. A phone's GPS fix and a laptop's
   IP-derived fix overwrite each other every 30s.
2. **Stop breaks the other device.** Whichever stops sets `StoppedAt`; the other
   keeps a live `ActiveShare`, keeps its tracker running (on Android a
   foreground service), keeps calling `Upsert` — and every update is dropped by
   `if (!sharedLocation.IsLive(now)) return`. The device shows "sharing" with a
   countdown; nothing is shared.
3. **Durations mix.** Device B picking "15 minutes" adopts a row whose
   `Duration` was fixed at creation, so B's countdown and the row's real expiry
   disagree.
4. **Duplicate entries.** Device B posts a `Chats_UpsertEntry` for the adopted
   `LocationId`, so the chat gets a second "Live location" message pointing at
   the same share.

The invariant "one live share per author per chat" is right. Its *resolution* is
wrong: the row must belong to one device, and a new device must take the sharing
over rather than move in.

## Design

### 1. Takeover on create

`SharedLocationsBackend.OnChange`, create branch, when `LiveDuration > 0`:

```csharp
// The newest share wins: whatever this author had live in this chat is frozen right here,
// so the device that owned it can't keep reporting into a row it no longer owns.
var stopped = await StopOwnLiveShares(dbContext, authorId, now, cancellationToken).ConfigureAwait(false);
```

`StopOwnLiveShares` loads the author's live rows (the `AuthorId` index already
exists), sets `StoppedAt = now` and bumps `Version` on each, and returns them.
`GetOwnLiveShare`, which existed only to hand the running share back, is deleted.
The new share is then created exactly as today.

Everything runs in the command's single transaction, after the existing
`dbContext.SharedLocations.Lock(authorId, …)` advisory lock — so two devices
starting at the same instant serialize, and the later transaction takes the
earlier one's row over. Whichever commits last owns the sharing.

**Freeze, don't reuse.** The taken-over row keeps its last point and becomes the
frozen pin of its chat entry, which is what that entry should show once its
share ended. Rewriting `CreatedAt`, `Duration` and ownership in place would
leave the losing device holding a valid id for a row it still owns *in its own
eyes* — the ping-pong bug again. Freezing is what makes the stale id **inert**,
and that inertness is the whole compatibility story below.

**Invalidation must cover the frozen rows.** They are the losing device's signal,
so they cannot be missed. The operation item becomes the full affected set
instead of the single new share:

```csharp
if (Invalidation.IsActive) {
    // A takeover touches rows this command never named, so the affected set is read back
    // from the operation.
    var affected = context.Operation.Items.KeylessGet<ApiArray<SharedLocation>>();
    foreach (var location in affected)
        _ = Get(location.Id, default);
    if (!affected.IsEmpty)
        _ = ListLive(chatId, default);
    return null!;
}
```

Update and Remove set a single-element array, so there is one shape to read.

**The per-chat cap** (`Constants.Location.MaxSharingAuthorsPerChat`) must stop
counting the acting author's own row. `CountLiveShares` is an EF `CountAsync`, so
it runs as SQL against the database and cannot see the `StoppedAt` the takeover
just set on tracked entities — that write lands only at `SaveChangesAsync`. In a
chat at the cap, a user who is already sharing would therefore be refused their
own takeover, ending up sharing from neither device:

```csharp
private static Task<int> CountLiveShares(
    ChatDbContext dbContext,
    AuthorId authorId,
    Moment now,
    CancellationToken cancellationToken)
{
    // Excluding the author keeps their own live share from blocking their own create - this runs as SQL,
    // so a freeze applied earlier in the same transaction isn't visible to it.
    var chatId = authorId.ChatId;
    var nowUtc = now.ToDateTime();
    return dbContext.SharedLocations
        .CountAsync(
            x => x.ChatId == chatId.Value
                && x.AuthorId != authorId.Value
                && x.StoppedAt == null
                && x.CreatedAt + x.Duration > nowUtc,
            cancellationToken);
}
```

Today this is unreachable — an already-sharing author returns early from the
singleton check and never gets to the cap — so it only becomes load-bearing once
`StopOwnLiveShares` replaces that return. **Landed ahead of the takeover.**

One-shot pins (`Duration == 0`) take nothing over — the branch is gated on
`duration > TimeSpan.Zero`, as the singleton check is today.

**Takeover is unconditional** — there is no setting for it. It doesn't need one:
what makes it safe for a client that can't be updated isn't a switch, it's that
freezing the row makes that client's id inert (§2). A flag would only add a
second behavior to reason about and test, and a state some environment is
eventually left in by accident.

### 2. Ignoring the loser's pushes, cheaply

A device that lost its share and can't tell (an old app, or a new one that
hasn't noticed yet) keeps reporting into a frozen row. That is **correct and
harmless by construction** — `IsLive` is false, so both write branches return
before touching anything:

```csharp
// A change past LiveUntil is ignored so a frozen share keeps its last position.
if (sharedLocation is null || !sharedLocation.IsLive(now))
    return sharedLocation;
```

Nothing that device sends reaches the chat, its position never moves the
marker, and the live share it lost belongs to the new device alone. This is the
property the whole compatibility story rests on, so it gets a test of its own
rather than being left as an implementation detail.

What it is *not* today is cheap. Every ignored push still opens an operation DB
context, takes a `pg_advisory_xact_lock` on the author, and reads the row —
once per `UpdatePeriod`, per stuck device, indefinitely. So the guard moves
earlier:

```csharp
var isCreate = change.IsCreate(out var createDiff);
if (!isCreate && id is not null) {
    var existing = await Get(id, cancellationToken).ConfigureAwait(false);
    if (existing is not null) {
        RequireOwnedBy(existing, authorId, chatId);
        if (!existing.IsLive(Clocks.SystemClock.Now))
            return existing;
    }
}
```

Three things make this sound:

- **The states are terminal.** `StoppedAt` is never cleared and `Duration` never
  shrinks, so "not live" can't become "live" again. A stale read can only err
  toward "still live", which falls through to the normal path and is handled
  correctly there.
- **It returns the frozen share**, not null — that return value *is* mechanism B
  below, so it must keep flowing.
- **It keeps the ownership check** (`RequireOwnedBy`, factored out of the
  existing inline check) so a non-owner still gets `Unauthorized` rather than a
  silent success — the early-out must not become a way to read someone else's
  share.

It reads through `Get`, the compute method, rather than the entity resolver
underneath it: a stuck device's repeated pushes then hit the Fusion cache
instead of the database, and the takeover already invalidates that cache.

### 3. Remote stop: how the losing device finds out

Three mechanisms, layered so each covers what the one above it can't. All three
end in `DropShare(chatId, locationId)`, which removes the entry from `_shares`;
`DispatchShares` then tears the report loop down and calls `Tracker.Stop`, which
on Android takes the foreground service and its notification with it
(`LocationActivitySource` → `ActivitiesBackend`).

**A. Watch (push).** A third chain in `LiveLocationReporter.OnRun`, shaped like
the existing `TroubleshootTracking`:

```csharp
[ComputeMethod]
protected virtual async Task<ApiArray<SharedLocationId>> ListStoppedShares(CancellationToken cancellationToken)
{
    var shares = await _shares.Use(cancellationToken).ConfigureAwait(false);
    var now = ServerNow;
    var result = ApiArray<SharedLocationId>.Empty;
    foreach (var share in shares) {
        if (share.LocationId is not { } id || share.ExpiresAt <= now)
            continue;

        // A null read is "I don't know" - offline, or not cached yet - and must not stop a live share.
        var location = await SharedLocations.Get(Session, share.ChatId, id, cancellationToken).ConfigureAwait(false);
        if (location is not null && !location.IsLive(now))
            result = result.Add(id);
    }
    return result;
}
```

`SharedLocations.Get` is a remote compute method, so the takeover's invalidation
is pushed to the subscriber and the chain reacts within a round trip. One
subscription per active share — negligible.

**B. Report result (pull).** `Report` already calls `SharedLocations_Change`
every `UpdatePeriod` and throws its result away. It stops doing that:

```csharp
var location = await Commander.Call(...).ConfigureAwait(false);
if (location is not null && !location.IsLive(ServerNow))
    await DropShare(share.ChatId, locationId, cancellationToken).ConfigureAwait(false);
```

Zero extra traffic, ≤30s worst case, independent of whether any subscription
survived — the net that catches a missed invalidation. It reads exactly the
value §2 returns early.

**C. Startup sweep.** `ReportLoop` validates its shares against the server
*before* `Tracker.Start`, reusing the same read as A, and drops the dead ones.
Without it, a device relaunching into a share taken over hours ago spins GPS and
the foreground service up just to have A or B kill them a second later.

#### How the losing device finds out

| Device A's state when B takes over | Mechanism | Latency | Result |
|---|---|---|---|
| Foreground, connected | A — invalidation of `Get(myId)` | ~one round trip | share dropped, tracker stopped, toast shown |
| Backgrounded, process alive, connected (Android FGS, iOS background updates) | A — the scope and its RPC connection outlive backgrounding, which is how background reporting works at all | ~one round trip | same, plus the FGS notification disappears |
| Connected, but the invalidation was missed (rolling deploy, dropped subscription) | B — the next report's result | ≤ `UpdatePeriod` (30s) | same |
| Offline | none until reconnect, then whichever of A/B fires first | ≤30s after reconnect | same; GPS keeps running until then, bounded by reconnect |
| Process killed / tab closed | nothing runs, so nothing is tracked | — | C drops it on next launch, before GPS or the FGS start |
| Share expired locally first | existing `DropExpiredShares` | at expiry | unchanged, no toast |
| **Older app — no A, B or C** | none | never | keeps tracking; every push is ignored (§2); user stops it on that device |

**Fail-open is the rule.** A share is dropped only on a definite "the server says
this row is not live". Errors, nulls and cache misses keep it running. The cost
of being wrong the other way is a user who thinks they're sharing and isn't —
the exact bug this plan fixes.

**Telling the user.** When a share is dropped by A, B or C — it did *not* expire
and the user didn't stop it here — show a toast via the existing
`ToastUI.Show(text, icon, ToastDismissDelay.Short)`. Wording comes from one
already-subscribed read, `LocationUI.GetOwnLive(chatId)`: a different live share
exists → "Location sharing moved to another device"; none → "Location sharing
was stopped from another device". The device's chat entry stays as a frozen pin
— the durable record.

### 4. Stop works from any device

Today `LocationUI.StopSharing(chatId)` forwards to the reporter, which only knows
this device's shares. Under takeover that silently does nothing on a device that
isn't the owner — the stop buttons in the chat header, the map panel and the
activity panel would all be dead there. So:

- `LocationUI.StopSharing(chatId, cancellationToken)` resolves the author's live
  share via `GetOwnLive` and sends `Change.Remove` for its id, *and* drops the
  local `ActiveShare` if there is one. A new-enough owner self-heals through
  A/B/C; an old one keeps tracking but is ignored, exactly as after a takeover.
- `LocationUI.StopSharing(chatId, locationId, cancellationToken)` — new overload
  stopping one specific share. `LocationMessageView`'s stop button uses it with
  `Entry.LocationId`, so stopping from a message stops the share that message is
  about.
- `LiveLocationReporter.StopSharing(chatId, locationId = null)` drops the
  matching local share (all of the chat's, when no id is given) and removes the
  server rows it owns, as today.

`StartSharing`'s existing `StopServerShares(replaced)` stays: if the new create
never lands (app killed between the two), the explicit stop is what keeps this
device's own previous row from lingering.

### 5. Making an old client's Stop actually stop

After a takeover, an old client's Stop is a silent no-op on the shared state.
`StopSharing` drops its local `ActiveShare` — so the device does stop tracking —
and then sends `Change.Remove` for *its own* id, which the takeover has frozen.
The server ignores it (§2), the live row keeps running, and the chat still shows
the user sharing from the other device. Same through the Android notification's
Stop action and the stop button on a location message.

That is the worst of the old-client problems, because it is an explicit user
intent failing quietly rather than a cost the user can see. It is also the only
one that can be closed from the server.

**The server can tell an old caller from a new one.** The RPC handshake carries
the caller's API version set (`RpcPeer.Versions`, built from
`handshake.RemoteApiVersionSet`), and `RpcInboundContext.Current` exposes the
peer as an `AsyncLocal`, so the version is readable inside the handler:

```csharp
private static bool IsLegacyCaller()
{
    // No inbound RPC context = an in-process caller (tests, backend, server-side code),
    // which gets the exact by-id semantics rather than the compatibility rule.
    var peer = RpcInboundContext.Current?.Peer;
    return peer is not null && peer.Versions[RpcDefaults.ApiScope] <= LastVersionWithoutRemoteStop;
}
```

**The rule.** For a legacy caller, a `Remove` whose target share is **already
frozen and owned by that caller's author** is re-read as *"stop my live share in
this chat"*. Every other case is untouched: new callers, live targets, and other
authors keep exact by-id semantics.

**Why it is version-gated rather than universal.** A new client can legitimately
send a `Remove` for a share that froze between render and tap; silently stopping
a *different* row would be a surprise it has no way to predict. An old client
cannot be surprised — once its id is frozen, it has no by-id expectation left to
violate, and the alternative is the no-op above.

**Why the version is read rather than routed with `[LegacyName]`.**
`[LegacyName(wireName, maxVersion)]` is the idiomatic tool for this and is
already used twice here — `GetNews`/`GetFullNews` at `"2.12.9999"`,
`GetListeningStream`/`LegacyGetListeningStream` at `"2.15.9999"` — but it works
by routing one wire name to two *different* methods. A command handler is bound
to its command type, so two `[CommandHandler]` methods taking
`SharedLocations_Change` would be ambiguous to CommandR. Reading the peer
version keeps one handler and one command type. `[LegacyName]` stays the right
answer for non-command RPC methods; this is the exception, and worth
remembering as one.

**Where it goes**: `SharedLocations.OnChange`, the API layer — that is where the
client's inbound context lives. `ISharedLocationsBackend` is untouched: it still
receives a `Remove` with a concrete id, just the live one. Finding that id costs
one `Backend.ListLive(chatId)` filtered to `chatRules.Author`, and only on this
path — a legacy `Remove` naming a frozen share — so the ordinary stop is
unchanged.

`LastVersionWithoutRemoteStop` is pinned to the last release shipped without
mechanisms A/B/C, in the `X.Y.9999` form the existing shims use (`ApiConstants.Version`
is the assembly `X.Y`). It must be pinned deliberately at implementation time,
not read from "current".

**What this does and does not fix.** It closes the Stop problem for every old
client, with no app update, no migration and no contract change. It does nothing
for the *tracking* problem: knowing the caller is old creates no channel to a
*different* device's report loop, which is the device that keeps GPS on.

### 6. UI: author-level vs device-level

With at most one live share per author per chat, `GetOwnLive` / `IsOwnLive` stay
author-level and become deterministic (`ListLive` can no longer hold two rows for
one author). That's also the right signal for the buttons: if my other device is
sharing, this device should offer to stop it, or to take it over by sharing.

What must become device-level is anything derived from *this* device's hardware:

```csharp
[ComputeMethod]
public virtual async Task<bool> IsOwnDeviceLive(ChatId chatId, CancellationToken cancellationToken)
    => await Reporter.GetActiveShare(chatId, cancellationToken).ConfigureAwait(false) is not null;
```

backed by a new `LiveLocationReporter.GetActiveShare(chatId)` compute method that
reads `_shares` and self-invalidates at the share's `ExpiresAt`, mirroring
`LocationUI.GetCountdown`.

- `LocationUI.GetTrackingError` switches from `IsOwnLive` to `IsOwnDeviceLive` —
  `ILocationTracker.Error` is local and says nothing about another device's
  share, so today it would show "Location tracking is off" over someone else's
  perfectly healthy share.
- `MustTroubleshoot` already reads `_shares` — correct as is.
- **"Sharing from another device"**: when `IsOwnLive && !IsOwnDeviceLive`,
  `MapPanel` labels its button "Share from this device" instead of "Share your
  location" and keeps the stop button; `ShareLocationModal` says so in the
  live-share tile's caption. Without it the takeover is invisible until it
  happens.

Nothing else changes: `ActivityPill`, `ChatActivityPanel*`,
`StopLocationSharingButton`, `ChatActivityUI`, `LocationMapModal` and
`ListParticipants` keep working, because the "one live share per author"
assumption they were written against still holds.

### 7. Chat entries

A takeover posts a new "Live location" entry for the new share, and the old
entry's map freezes at its last point. So sharing from a second device leaves two
messages, one frozen and one live. That's honest — an entry is the handle of one
share, with its own countdown and stop button — and it already happens on every
restart of sharing today.

Re-pointing the existing entry at the new share would keep the chat tidier but
rewrites a message posted from another device at another time. Deferred.

## Reuse

### Existing abstractions to reuse

- `LiveLocationReporter` + `ActiveShare` + `StateFactory.NewKvasStored` over
  `LocalSettings` — the device-local share list; the new drop paths funnel into
  the same `_shares` mutation `StopSharing` and `DropExpiredShares` already use.
- `RpcInboundContext.Current.Peer.Versions[RpcDefaults.ApiScope]` — the caller's
  API version, already carried by every RPC handshake. §5 reads it; no new
  plumbing, no handshake change, nothing added to the wire.
- `[LegacyName(wireName, maxVersion)]` — the idiomatic sibling of that read, used
  by `IChats.GetNews`/`GetFullNews` and `ILiveAudioStreams.GetListeningStream`.
  §5 explains why a command handler reads the version instead.
- `SharedLocationsBackend.Get` — §2's early-out reads through the existing
  compute method, so repeated stale pushes are served from the Fusion cache.
- `AsyncChain` / `RetryForever` / `FuncWorker` in `OnRun` — mechanism A is a
  third chain built exactly like `TroubleshootTracking`.
- `Computed.Capture(...).Changes(ct)` — the react-to-a-compute-method pattern
  used by `TroubleshootTracking` and `DispatchShares`.
- `Computed.GetCurrent().Invalidate(delay)` — the expiry-driven invalidation
  `SharedLocationsBackend.Get` and `LocationUI.GetCountdown` already use; reused
  by `GetActiveShare`.
- `DbSetExt.Lock` (`pg_advisory_xact_lock`) — already wrapping the create path;
  what makes the takeover atomic against a concurrent start.
- `CommandContext.Operation.Items` — already carries the affected share for
  invalidation; it just carries an `ApiArray` now.
- `ToastUI.Show(text, icon, ToastDismissDelay)` — the takeover notice; no new
  notification surface.
- `LocationActivitySource` / `ActivitiesBackend` — the Android foreground service
  and its notification already follow `GetActiveShareChatIds`, so dropping a
  share tears them down with no new plumbing.
- `SharedLocationOperations` (`tests/Testing.Host`) — already takes an explicit
  `SharedLocationId`, so the multi-device tests need no new harness.

Nothing here needs an abstraction that doesn't exist, and there's no reusable
"one live thing per owner, newest wins" helper to build on.

### Reusability of new components

Everything new is either device-local UI state or a private backend helper, so
all of it belongs where it's used — none is a candidate for `ActualChat.Core` or
`ActualChat.Core.Server`:

- `LiveLocationReporter.GetActiveShare` / `ListStoppedShares` / `DropShare` —
  read and mutate `_shares`, this service's private state.
- `LocationUI.IsOwnDeviceLive` — a projection of the above, consumed only by
  location UI components. It replaces `GetLive(AuthorId)`, whose only caller was
  `GetOwnLive`; that read is now inline.
- `SharedLocationsBackend.StopOwnLiveShares` / `RequireOwnedBy` — operate on the
  command's open `ChatDbContext` and transaction; private to the backend.

The concurrent-shares draft of this plan needed a shared
`SharedLocationExt.DistinctByAuthor` to collapse several live shares per author
into one marker. Takeover removes the need entirely — there is never more than
one.

## Changes by file

| File | Change |
|---|---|
| `src/dotnet/Chat.Service/SharedLocations.cs` | legacy-caller detection; a legacy `Remove` of a frozen own share retargets to the author's live one |
| `src/dotnet/Chat.Service/SharedLocationsBackend.cs` | `GetOwnLiveShare` → `StopOwnLiveShares` (takeover); early-out for frozen shares + `RequireOwnedBy`; affected-set invalidation; `CountLiveShares` excludes the acting author, whose freeze isn't visible to SQL yet (**done**) |
| `src/dotnet/UI.Blazor.App/Services/Location/LiveLocationReporter.cs` | `GetActiveShare`; `ListStoppedShares` + watch chain; startup sweep in `ReportLoop`; `Report` inspects its result; `DropShare` + takeover toast; `StopSharing(chatId, locationId)` |
| `src/dotnet/UI.Blazor.App/Services/Location/LocationUI.cs` | `IsOwnDeviceLive`; `GetTrackingError` gated on it; `StopSharing` resolves the author's live share; by-id overload |
| `.../ChatView/Items/LocationMessageView/LocationMessageView.razor` | stop by `Entry.LocationId` |
| `.../VisualActivityPanel/MapPanel.razor` | "Share from this device" when the author is live elsewhere |
| `.../ShareLocationModal/ShareLocationModal.razor` | same hint in the live-share tile's caption |

No contract, model or DB changes — so no migration, and old clients keep talking
to the server with the messages they already send.

## Tests

**`tests/Chat.IntegrationTests/SharedLocationsTest.cs`**

- `NewLiveShareReturnsExistingWhenAlreadySharing` → **inverted**, renamed
  `NewLiveShareTakesOverTheRunningOne`: the second create returns a **new** id,
  `ListLive` holds exactly one share and it's the new one, and the first is
  readable by id, frozen at its last point, `IsLive == false`.
- **new** `TakenOverShareIgnoresFurtherUpdates` — the load-bearing compatibility
  test. After a takeover, `Update` on the old id returns the frozen share,
  doesn't move its point, doesn't bump its `Version`, and doesn't disturb the new
  share. This is exactly what an un-updated client does forever, so it must
  never regress.
- **new** `FrozenShareUpdateStillChecksOwnership` — Bob updating Alice's frozen
  share still gets `UnauthorizedAccessException`, i.e. the §2 early-out didn't
  open a hole.
- **new** `AnyDeviceCanStopTheAuthorsLiveShare` — a second session of the same
  user removes the share by id and `ListLive` empties.
- **new** `LegacyStopOfAFrozenShareStopsTheLiveOne` — with a simulated legacy
  caller, `Remove` of a taken-over id empties `ListLive`. The regression it
  guards is silent, so it needs a test rather than a manual check.
- **new** `StopOfAFrozenShareIsANoOpForCurrentClients` — the same call from a
  current caller leaves the live share alone, pinning that §5 is scoped to old
  clients and cannot surprise a new one.
- **Not covered**: the `CountLiveShares` exclusion. Reaching
  `MaxSharingAuthorsPerChat` needs 100 sharing authors, and a cheaper test
  would pass with or without the fix — so it's left to review rather than
  faked.
- `LiveLocationShareLifecycle`, `OnlyAuthorCanUpdateAndStopLocation`,
  `LiveShareAutoExpires`, `NonMenuDurationIsRejected` — unchanged.

The multi-device tests sign a second tester in as Alice with an explicit shared
identity: `SignInAsAlice()` with no argument mints a fresh Ulid identity per
call, so the default gives two *users*, not one user's two devices.

**`tests/ts/e2e`** — a two-context, same-user test is the real acceptance check,
since it exercises mechanism A end to end: context A shares, context B shares,
and within seconds A stops showing "sharing", shows the toast, and its entry's
map is frozen while B's is live.

**Manual matrix** — the rows of [the state table](#how-the-losing-device-finds-out)
automation can't reach: Android backgrounded with the foreground service up
(notification must disappear), airplane mode during takeover (share survives
until reconnect, then stops), force-quit and relaunch (no GPS or FGS on launch).
Plus one **old-app run**: take a store build's share over from a new client and
confirm the chat shows only the new share, the old device's pushes change
nothing, and its own stop button and notification Stop action still work.

## Compatibility and rollout

- **No migration, no contract change.** Old clients send exactly what they send
  today.
- **An old app is never broken, only ignored.** When it loses a share it keeps
  reporting into a frozen row; §2 turns those pushes away cheaply and they never
  reach the chat. Its data is not wrong, it is absent — and the live share it
  lost is correctly owned by the new device.
- **What it costs that device**: it keeps GPS on, and on Android its location
  foreground service and notification up, until its *local* `ActiveShare`
  expires — `StartedAt + Duration`, computed from its own persisted state. That's
  ≤8h for a capped duration, and **indefinitely for "until I turn it off"**,
  including across relaunches, since the stored share is unexpired and tracking
  resumes on the next launch. This is accepted: see [Risks](#risks).
- **Its Stop button works**, thanks to §5: the old client stops by its frozen id,
  and the server reads that as "stop my live share in this chat". Without §5 it
  would stop the device's tracker but leave the sharing running from the other
  device. The Android notification's Stop action
  (`ActivitiesBackend.InvokeAction` → `StopAllSharing`) takes the same path.
- **Its UI isn't lying, either.** Old `GetOwnLive` reads `ListLive` at author
  level, which returns the **new** device's share, so it keeps showing "sharing"
  with a valid countdown — which is true, the user *is* sharing, from another
  device. Its own frozen entry correctly renders as a static pin.
- **Rolling deploy.** While both server versions run, a node on the old code
  replaying a new node's operation reads the old single-share item shape, finds
  nothing, and skips that invalidation. Clients subscribed through such a node
  miss mechanism A and fall back to B (≤30s). Transient and self-correcting.

## Risks

- **An old losing device keeps tracking** — until its local share expires, and
  indefinitely if that share was unlimited. Accepted: nothing the server returns
  or throws reaches an un-updated client's report loop, so the only alternatives
  were to force an upgrade or to block the *updated* device from sharing, and
  neither is worth this. Bounded in practice by the two escape hatches above, by
  the fact that the device shows "sharing" whenever the user looks at it, and by
  app updates. Capping `UnlimitedDuration` server-side would bound the worst case
  — see [Deferred](#deferred).
- **A new device that's offline for a long time** keeps GPS on until it
  reconnects, since fail-open means A/B/C all need a definite server answer. It
  is not *sharing* anything — its writes are ignored — but it spends battery.
  Bounding that (stop tracking after N consecutive failed reports) is an easy
  follow-up that should be measured before being assumed necessary.
- **Ignored-push traffic.** A stuck device sends one command per 30s forever.
  §2 makes each one cheap, but they still cost a round trip and a resolver read,
  and they shouldn't be logged at anything above debug.
- **§5 depends on a correctly pinned version constant.** Set
  `LastVersionWithoutRemoteStop` too high and current clients get the
  compatibility semantics; too low and old clients keep the silent no-op. The
  two tests above pin both sides.
- **Toast noise.** A user deliberately moving sharing between devices gets a
  toast on the old one every time. That's the point, and it's one short toast.
- **Frozen-row garbage.** Every takeover leaves a stopped row behind. They're
  small, they back the frozen pin of a real chat entry, and every stop already
  produced them.

## Deferred

- **Stop tracking after N consecutive failed reports** — bounds the offline
  new-client case, and would need measuring first.
- **Cap `UnlimitedDuration` server-side** (say 24h, renewable) — bounds the worst
  case of a stale device tracking forever, and simplifies several other
  expiry-adjacent behaviors. A product decision, not a technical one.
- **Stop sharing everywhere, across chats.** Per chat, stop-from-any-device falls
  out of this design. "Stop all my sharing in every chat" needs a server-side
  query over the user's authors — a new `ISharedLocationsBackend` method — plus a
  Settings entry point.
- **Re-point the entry instead of posting a new one** — keeps the chat tidy on
  every takeover; needs an entry edit carrying a new `LocationId` and a decision
  about editing a message posted from another device.
- **Per-device labels** ("moved to iPhone") would need a device key on
  `DbSharedLocation` and a stable cross-platform device id. Not needed here.

## Alternatives considered

**Capability-gated takeover** — record on each row whether its owner is new
enough to notice being taken over (`CanBeTakenOver`, set by the creating client),
and refuse to take a share whose owner can't react, telling the new device to go
stop the old one instead. It removes the stale-device tracking entirely.
Rejected: it blocks the *updated* device from sharing in order to save battery on
a stale one, which penalizes the user who did nothing wrong; it needs a column, a
migration and a refusal UX; and the thing it prevents is a battery cost, not a
correctness one — frozen rows already make the stale device's pushes inert.

**Take over unconditionally and force the app forward**, leaning on
`SystemProperties.MinCompatibleVersion` to make old clients update. Rejected:
forcing an upgrade to make a server-side change safe is the wrong order, and the
takeover is safe without it.

**Client-minted `SharedLocationId`s.** Needed when the client had to tell "my new
share" from "someone else's running share" — i.e. when a create could be refused.
With unconditional takeover a create always returns the caller's own new row, so
there's nothing to disambiguate. It would still make a retried create idempotent
(today a lost response leaves one extra frozen, entry-less row), which isn't
worth changing the persisted `ActiveShare` shape for.

**Allow concurrent shares from several devices** (the first draft of this plan):
drop the per-author singleton, let each device own a row, and dedupe to one
marker per author in the UI. Rejected by product decision — one person is in one
place, and two live shares mean two countdowns, two stop buttons and a marker
that has to pick a winner anyway.

**Take the row over in place** — rewrite `CreatedAt`, `Duration` and ownership on
the existing row instead of freezing it. Fewer rows, one chat entry. Rejected:
the losing device still holds a valid id for a row that is still live, so it
keeps writing to it — the ping-pong bug with extra steps. Freezing is what makes
the stale id inert.

**Client-side takeover** — the new device lists the author's live shares, removes
them, then creates its own. Works without touching the server, but it's three
round trips, it isn't atomic (two devices starting together can both end up
stopped, or both live), and it puts an invariant in the one place that can't
enforce it.
