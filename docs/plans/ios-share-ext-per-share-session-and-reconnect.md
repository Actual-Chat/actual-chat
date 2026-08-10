# iOS share extension: per-share session refresh and reconnect kick

Follow-up to #4137 (`fix(ios): stop rebuilding the whole app on every share`).
That commit made the DI container, logging, Sentry and session init process-wide,
with a share being an `AsyncServiceScope` over them. Two things that used to
self-heal only because every share rebuilt the container no longer do.

Status: **implemented** (not yet verified on device) - see Plan below.

## Problem 1 — a rotated or dead session sticks for the life of the process

The session id doesn't travel with the call. Scoped `ISessionResolver` is
`DefaultSessionResolver`, which always returns `Session.Default`
(`src/dotnet/Core/Security/DefaultSessionResolver.cs:13`); the real id goes out as
a WebSocket handshake header read from the singleton `TrueSessionResolver`
(`src/dotnet/Api.Contracts/Module/ApiContractsModule.cs:137-138`). That's why the
`Session` setter ends with `GetClientPeer(RpcRef.Default).Disconnect()`
(`src/dotnet/Core/Security/TrueSessionResolver.cs:39`) — a new session means a new
handshake.

`SessionInitializer` (`src/dotnet/App.Maui.IosShareExt/Services/SessionInitializer.cs`)
is a run-once worker: `SetSession` returning ends the `AsyncChain`, and
`RetryForever` only retries failures. As a singleton it reads
`Fusion.SessionId` from the shared keychain exactly once per process, so shares
2..n never look again. It couldn't re-set it anyway — `TrueSessionResolver.Session`
throws `AlreadyInitialized` on a different value (`TrueSessionResolver.cs:32-33`);
only `Replace()` (line 46) rotates, and the extension never calls it.

The main app *does* rotate that keychain entry: `MauiSession.Acquire()` stores a
new id and calls `Replace` when `ValidateSession` returns a different session
(`src/dotnet/App.Maui/Services/MauiSession.cs:57-65`); sign-out goes
`RemoveStored()` -> create -> store.

Symptom: `Accounts.GetOwn` comes back guest, `ShareUI.GetStep` returns
`ShareStep.SignIn` (`src/dotnet/App.Maui.IosShareExt/Services/ShareUI.cs:106-108`),
and the sheet shows "Sign in to Voxt to share content" although the user is
signed in in the app. It heals only when iOS reaps the appex process.

Same trap in the other direction: if the first share of a process happens before
any session exists, `SetSession` logs `"No session id found."` and returns —
permanently for that process, even after the user signs in in the main app.

## Problem 2 — a share inherits the RPC peer's reconnect backoff

The client peer is now process-wide, so a dead socket carries across shares.
`RpcClientPeer` reconnects on its own, but the extension uses the base delayer
from `src/dotnet/Core/Module/CoreModule.cs:52-54` — `RetryDelaySeq.Exp(1, 180)`,
so up to 3 minutes.

The main app replaces it with `AppRpcClientPeerReconnectDelayer`
(`src/dotnet/UI.Blazor/Module/BlazorUICoreModule.cs:93`) and kicks `CancelDelays()`
from `ReconnectUI` (`src/dotnet/UI.Blazor/Services/ReconnectUI.cs:29,38`) and
`ConnectivityUI` when connectivity returns. The share extension registers neither
and has no reconnect UI.

It won't look broken, which is worse: the client compute cache answers
`GetOwn`/`Contacts` from disk, so the picker renders, and the send then hangs
until the 20s connect timeout (`RpcCallTimeouts.Default.Command`,
`src/dotnet/App.Maui.IosShareExt/ClientStartup.cs:29`) drops it into
`ShareStep.Failed`.

## Reuse

Existing abstractions to reuse — no new types needed:

- `AppleSharedSecureStorage.Default` (`src/dotnet/Maui/MaciOS/AppleSharedSecureStorage.cs`)
  and the `"Fusion.SessionId"` key already read by `SessionInitializer`.
- `TrueSessionResolver.Replace(Session)` (`src/dotnet/Core/Security/TrueSessionResolver.cs:46`) —
  swaps the value and disconnects the peer, and unlike the setter doesn't throw
  when a session is already set or absent.
- `RpcHub.InternalServices.ClientPeerReconnectDelayer.CancelDelays()` — the
  accessor path `ReconnectUI.cs:16-17` uses.
- `SessionInitializer` itself stays the singleton it is; it grows a method the
  share path calls, rather than changing lifetime back.
- `ComputedRegistry.InvalidateEverything()` — already used by
  `SystemProperties.OnInvalidateEverything`; see Plan step 3.

New components: none, so no shared-vs-local placement question. Everything lands
in `App.Maui.IosShareExt`, which is the only host with a reused appex process.
`AppRpcClientPeerReconnectDelayer` is deliberately *not* pulled down from
`UI.Blazor` — it's tied to `ConnectivityUI`/Blazor, and the extension only needs
the one `CancelDelays()` call.

## Plan

1. **Done.** `SessionInitializer.SetSession` now compares the stored id against
   the resolver's current session and calls `Replace(...)` on a mismatch, and
   `Refresh(CancellationToken)` exposes it as a per-share entry point that
   swallows its own errors. The worker still owns the first run, so a cold share
   keeps its retry-forever behavior. Two smaller changes came with it: the
   keychain key is a `SessionStorageKey` const (it matches `MauiSession`'s), and
   "no session id found" dropped from `LogCritical` to `LogWarning` — it now
   repeats per share, and a user who hasn't signed in yet isn't an incident.
2. **Done.** `ShareExtensionApplication.Bootstrap` calls
   `RefreshSessionAndConnection(services, log)` once the process-wide container
   is in hand: a `BackgroundTask` that awaits `SessionInitializer.Refresh`, then
   `ResetConnectionAttemptIndex()` + `CancelDelays()` (the pair
   `ReconnectUI.ResetReconnectDelays` uses — cancelling the current delay alone
   would leave the next attempt at the same point in the exponential sequence).
   Ordering matters: the session lands first, so the peer this touches — created
   here on the first share — handshakes with the right header. The call sits
   ahead of `CreateAsyncScope()` so the refresh gets the whole scope-create +
   view-build window; it's still a race it can lose, and losing it costs the
   pre-selected suggested recipient (`ShareUI.OnRun` returns early on a guest
   account and never re-runs). `GetStep` recomputes either way, so the sheet
   still lands on the contact list rather than the sign-in screen.
3. **Done, and it does not fall out for free.** A `Replace` disconnects the peer,
   but the reconnect keeps the client's completed compute calls: the client peer
   is process-wide, so its `ClientId` is unchanged, the server resumes the same
   `RpcServerPeer`, the handshake reports `RpcPeerChangeKind.Unchanged`, and
   `RpcCallTrackers.Reconnect` re-registers every call whose
   `RemoteExecutionMode` allows it — which for compute methods is all of them.
   The server-side computed behind `Accounts.GetOwn` is keyed by the *real*
   session (`RpcDefaultSessionReplacer` substitutes it for `Session.Default` at
   call time), so the client would keep serving the previous user's account and
   the sheet would keep showing the sign-in screen — the exact symptom this plan
   is about. `SetSession` therefore calls `ComputedRegistry.InvalidateEverything()`
   after a `Replace` that *changed* an existing session (not the initial set).
   That's the same lever `SystemProperties.OnInvalidateEverything` pulls
   server-side, and it's the extension's equivalent of the `ReloadUI.Reload` the
   main app does after `MauiSession` rotates a session.

## Verification

On device, one appex process (the point of #4137 — check the pid stays put
across shares):

- Share, then sign out and back in in the main app, then share again — the
  second sheet must show contacts, not the sign-in screen. Sign in as a
  *different* user for the sharper version of the same check: the contact list
  must be the new user's.
- Share, kill the network long enough for the peer to back off, restore it, then
  share again — the sheet must connect promptly instead of waiting out the
  backoff.
- Fresh install: share before ever signing in (expect the sign-in screen), then
  sign in in the app, then share again — must show contacts.
- Re-run the #4137 regression check: 11 consecutive shares, one bootstrap, one
  WebSocket, no `KvasarLockException`, no watchdog kill, scene-create ~30ms on
  shares 2..n.
