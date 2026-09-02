# Call outcome entries in peer chats

A call that never connects currently leaves no trace. This design records the
three unsuccessful outcomes — no answer, declined, canceled — as a new kind of
system chat entry, rendered as a card in the message list.

## Problem

`LiveSessionsBackend` already knows how every call ended: `CallInviteStatus`
(`Ringing`/`Accepted`/`Declined`/`Missed`) and `CallStatus`
(`Dialing`/`Accepted`/`Declined`/`NoAnswer`) are written to Redis as the ring
lifecycle progresses. Both are ephemeral — they carry a TTL and exist only to
drive the caller's transient banner. `CloseAndMaterialize`
(`src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs:1127`) short-circuits
for calls: it dismisses lingering rings and drops the session, materializing
nothing.

So after a missed call the chat looks exactly as it did before the call. There
is nothing to tap to call back, and nothing to tell the callee that someone
tried to reach them.

## Scope

In scope:

- Three outcomes: **no answer** (ring expired), **declined** (invitee refused),
  **canceled** (caller hung up before anyone answered).
- Peer chats only. The model carries invitees so group calls can be enabled
  later without a data migration.
- Per-side wording: the caller and the callee read different text off the same
  stored entry.
- The entry behaves like an ordinary system entry for unread/chat-list purposes
  — the same as "X has left the chat". No dedicated push notification.

Out of scope (deliberately, and each is additive later):

- A "Call ended" entry for a call that *did* connect, with its duration. The
  `CallOutcome` enum leaves room for it.
- Group-call entries, where several invitees resolve differently.
- The live "Incoming call" / "Ongoing call" cards from the mockup — those are
  live-session state, already owned by the live block.

## User-facing behaviour

The entry renders as a card in the message list, in the same visual family as
the live conversation card (`c-live-card` in
`src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/conversation.css`):
an icon and a title on the first line, an optional hint on the right, and a
meta row with the participants' avatars and the time.

Wording, by outcome and by who is reading:

| Outcome | Caller sees | Other party sees |
| --- | --- | --- |
| No answer | **Outgoing call** · _No answer_ | **Missed call** · _Tap to call back_ |
| Declined | **Declined call** | **Declined call** |
| Canceled | **Canceled call** | **Missed call** · _Tap to call back_ |

The hint on the right is either a status (`No answer`) or an action. `Tap to
call back` starts a new call to the same party, with video if the original call
had video.

Both readings come from one stored row: the entry carries `CallerId`, and the
view compares it with the reader's own author in the chat
(`Chat.Rules.Author?.Id`). No per-viewer duplication of entries.

Server-composed text — the chat-list last-message preview, notifications,
search indexing — has no reader, so it uses a neutral third-person rendering
produced by `SystemEntryMarkupBuilder` (see Localization).

## Data model

New file `src/dotnet/Api/Chat/CallEntry.cs`:

```csharp
public enum CallOutcome {
    None = 0,
    NoAnswer = 1,
    Declined = 2,
    Canceled = 3,
    // Ended = 4 — reserved for a connected call, not emitted yet
}

[DataContract, MessagePackObject]
public sealed partial record CallEntry : SystemEntry
{
    [DataMember, Key(20)] public AuthorId CallerId { get; init; } = null!;
    [DataMember, Key(21)] public string CallerName { get; init; } = "";
    [DataMember, Key(22)] public CallOutcome Outcome { get; init; }
    [DataMember, Key(23)] public ApiArray<AuthorId> InviteeIds { get; init; }
    [DataMember, Key(24)] public bool HasVideo { get; init; }

    public CallEntry() : base((ChatEntryId)null!) { }

    [SerializationConstructor]
    public CallEntry(ChatEntryId id, long version = 0) : base(id, version) { }
}
```

`Canceled` follows .NET spelling (`OperationCanceledException`) and matches the
mockup. `InviteeIds` is the group-call seat. `HasVideo` earns its place now: the
call-back action needs to know which kind of call to place.

`CallerName` is **not** the name normally displayed — it is the `AuthorMention`
fallback. `SystemEntryMarkupBuilder.Build` is synchronous and cannot look an
author up, yet `AuthorMention` requires a name at construction; the stored one
fills that slot. Every consumer then runs the mention through `MentionResolver`,
whose `Enrich` does `author?.Avatar.Name ?? am.Name`
(`src/dotnet/UI.Blazor.App/Services/Internal/ChatMentionResolver.cs:36`), so the
live avatar name wins whenever the author resolves. The stored value surfaces
only when markup is rendered without the resolver — which is what
`SystemEntryLocalizationTest` does, and it asserts the name is there — or when
the author no longer resolves at all.

This is a *different* reason from the one `MembersChangedEntry` has for its
`TargetAuthorName`: there, an anonymous author is stored with
`TargetAuthorId = null` (`ChatsBackend.cs:2026`), and the name is then the only
identifying value left — which is also why that field is nullable. A caller
always has an id, so `CallerId` is non-nullable here, and the view can compare it
with the reader's own author without a null dance.

If group calls later admit anonymous callers, `CallerId` becomes nullable and
`CallerName` takes on the second, load-bearing role it has in
`MembersChangedEntry`. Until then it is a fallback only.

The shape mirrors `MembersChangedEntry` exactly, including the two constructors
— the parameterless one is what `SystemEntryLocalizationTest` builds samples
with, and the `[SerializationConstructor]` one is what MessagePack needs. Key
indices start at 20 because `ChatEntry` owns 0..19; per the MessagePack source
generator's rules, positional constructor parameter order must match `[Key]`
index order, so these stay property-initialized rather than positional.

Registrations this type requires:

- `src/dotnet/Api/Chat/ChatEntry.cs:16` — add `[Union(3, typeof(CallEntry))]`.
  The concrete kinds are declared on `ChatEntry`, not only on `SystemEntry`.
- `src/dotnet/Api/Chat/SystemEntry.cs:12` — add `[Union(2, typeof(CallEntry))]`.
- `src/dotnet/Api/Chat/ChatEntry.cs:209` — `ChatEntryKind.Call = 3`, plus the
  `NewEmpty` and `ChatEntryDiff(ChatEntry)` switches at lines 38 and 173.
- `src/dotnet/Api/Chat/ChatEntry.cs:166` — `ChatEntryDiff` gains
  `CallerId`, `CallerName`, `Outcome`, `InviteeIds`, `HasVideo`, all nullable.
  The diff is applied by `DiffEngine.DynamicPatch`
  (`src/dotnet/Chat.Service/ChatsBackend.cs:1445`), which matches **by property
  name**, so these names must equal the entry's exactly. Reusing the existing
  `TargetAuthorId`/`TargetAuthorName` pair would not map.

`DbChatEntry.Kind` stays 0 — it is a legacy column, always written as 0
(`src/dotnet/Chat.Service/Db/DbChatEntry.cs:190`), unrelated to
`ChatEntryKind`. No EF migration is needed.

### On-disk shape

System entries are stored in the `Content` column as JSON in the frozen v2.7
wrapper (`src/dotnet/Api/Chat/LegacySystemEntry.cs`). Despite the name and its
own doc comment, that wrapper is **only** a database format: it appears in four
places — its declaration, `DbChatEntry`, one data migration, and this spec — and
never crosses the wire. `Legacy` here means "the envelope shape is frozen", not
"deprecated"; new entry kinds still ride inside it.

They have to, because the row carries no other discriminator: `IsSystemEntry` is
a bare `bool` and `Content` is one string column. The wrapper is not a tagged
union either — each option is a *named property* (`MembersChanged`,
`NotifyMembers`) and `Option` picks whichever came back non-null. So
`DbChatEntry.ToModel` decides the subtype purely from which property is set, and
a third kind needs a third property with a payload type of its own:

- `LegacyCallOption : LegacySystemEntryOption` with `CallerId`, `CallerName`,
  `Outcome`, `InviteeIds`, `HasVideo`.
- A `Call` property on `LegacySystemEntry`, plus arms in `DbChatEntry.ToModel`
  (line 109) and `DbChatEntry.ToLegacySystemEntry` (line 250).

Two alternatives were considered and rejected. Writing the payload as its own
JSON straight into `Content`, bypassing the envelope, leaves nothing to tell the
two shapes apart on read short of sniffing the JSON. Pressing the existing `Kind`
column into service as the discriminator looks tempting — it is an `int` that is
always written as 0 — but it sits in the unique index `(ChatId, Kind, LocalId)`
and in the lid-range and `GetMaxLid` queries, which all assume 0; a non-zero
value on some rows would break those ranges.

`LegacySystemEntry.From` (line 34) duplicates `DbChatEntry.ToLegacySystemEntry`
and has no callers. It gets deleted rather than extended — it is in a file this
change touches anyway, and leaving a second, diverging conversion behind is how
the next kind ends up half-registered.

Consequence worth stating plainly: a server that does not know the `Call`
property deserializes such a row into `Option == null` and falls into
`_ => throw StandardError.Internal("Unknown system entry option: ")`
(`DbChatEntry.cs:119`). See Compatibility.

## Write path

The outcome is decided in three places and written in one.

**Decide.** `LiveSessionState` (`src/dotnet/Api/Live/LiveSessionState.cs`) gains
two fields, appended after `IsExpandedByDefault`, which currently holds 21:

- `[DataMember(Order = 22), Key(22)] CallOutcome Outcome`
- `[DataMember(Order = 23), Key(23)] bool HasVideo`

`HasVideo` has to be stored because `StartCall` receives it as an argument
(`LiveSessionsBackend.cs:522`) and today only forwards it to
`NotificationsBackend_NotifyCall` — by close time it is gone, and the call-back
action needs it. `StartCall` sets it alongside the rest of the state.

This record lives only in Redis, so there is no migration; an old value simply
deserializes with `Outcome = None` and `HasVideo = false`.

Three call sites set it, each already holding the fact:

| Site | Sets |
| --- | --- |
| `DeclineCall` (`LiveSessionsBackend.cs:614`) | `Declined` |
| `CancelCall` (`LiveSessionsBackend.cs:641`) | `Canceled` |
| `ExpireRings` (`LiveSessionsBackend.cs:971`) | `NoAnswer` |

**First writer wins**: a site sets `Outcome` only while it is still `None`.
Without that rule a caller who hangs up a moment after the invitee declined
would overwrite `Declined` with `Canceled`, and the chat would report the wrong
story. All three sites already run under `_changeLocks.Lock(chatId)`, so the
check and the write are atomic.

**Emit.** The single write site is `CloseAndMaterialize`
(`LiveSessionsBackend.cs:1127`), in its existing `state.IsCall` branch, guarded
by:

- `state.SessionStartedAt is null` — the call never latched to connected. A call
  that did connect and then ended is out of scope, and would otherwise be
  reported as canceled when the caller hangs up.
- `state.Outcome != CallOutcome.None`.
- `state.ChatId.Kind == ChatKind.Peer` — the peer-only scope gate. This is the
  one line that opens group calls later.

It then calls `ChatsBackend_ChangeEntry` with `Change.Create(new ChatEntryDiff {
Kind = ChatEntryKind.Call, AuthorId = Bots.GetWalleId(chatId), ... })`, the same
shape `ChatsBackend.cs:2032` uses for member changes. Wall-E authors the entry;
the card draws its avatars from `CallerId`/`InviteeIds`, not from the entry's
author.

Emitting from `CloseAndMaterialize` rather than from the three deciding sites is
what makes the write exactly-once: `Close` removes the Redis state under the
same lock, so the state that carries a pending `Outcome` cannot be seen twice.
`CancelCall` reaches close through `CloseNow`, which returns early if the
session is still live — but it stops every ring before that, so it isn't.

`InviteeIds` comes from `SafeGetInvites(state.ChatId)`, which
`CloseAndMaterialize` already reads in this branch to dismiss lingering rings.

`CallerName` is resolved at emit time from the caller's author, the same way
`ChatsBackend.cs:2027` resolves a member name, with `MentionMarkup.NotAvailableName`
as the fallback. See the note under Data model for what it is actually for — it
is not the name normally displayed.

## Render path

`ChatEntryMessageView.razor:70` currently sends every `SystemEntry` down one
branch that renders centered markup. A new branch above it routes `CallEntry` to
`CallMessageView`, a new component under
`src/dotnet/UI.Blazor.App/Components/ChatView/Items/Call/`.

`CallMessageView` is a `ComputedStateComponent` that resolves:

- whether the reader is the caller (`Chat.Rules.Author?.Id == entry.CallerId`),
- the title, the icon and the optional hint from the table above,
- the avatars, via the existing `AuthorCircleGroup`
  (used the same way at `ConversationMessageView.razor:74`).

The `Tap to call back` action calls `LiveSessionUI.StartCall(chatId, [caller],
entry.HasVideo)`.

The entry stays a `SystemEntry`, so everything else about it — no reactions, no
"copy message link" in the menu, no author badge, unread and chat-list
behaviour — keeps working unchanged.

The markup builders still get a `CallEntry` arm, because
`ChatMarkupHubExt.GetMarkup` is what feeds the chat-list preview, notification
text and search indexing, none of which render the card.

## Localization

Two groups of strings, because the card and the fallback text are different
surfaces.

**Fallback text** — `SystemEntryMarkupBuilder.BuildCall` /
`LocalizedSystemEntryMarkupBuilder.BuildCall`. The catalog convention here is
that the author name is its own markup node and the string is what *follows* it
(`src/dotnet/UI.Blazor.App/Services/LocalizedSystemEntryMarkupBuilder.cs:7`), so:

| Key | English |
| --- | --- |
| `SystemEntry_CallNoAnswer` | ` called. No answer.` |
| `SystemEntry_CallDeclined` | ` called. Declined.` |
| `SystemEntry_CallCanceled` | ` called. Canceled.` |

**Card text** — read by `CallMessageView` through `IStringLocalizer`:
`Call_Entry_Outgoing`, `Call_Entry_Missed`, `Call_Entry_Declined`,
`Call_Entry_Canceled`, `Call_Entry_NoAnswer`, `Call_Entry_TapToCallBack`.

All nine keys go into every shipped `Strings.*.json` under
`src/dotnet/Localization/Resources/`, following `docs/i18n.md`.

`SystemEntryLocalizationTest` already enforces the fallback half of this: it
fails on any `[Union]` kind missing from its `Entries()` samples, requires every
shipped language to render a full sentence containing the author name, requires
the author mention to survive localization, and requires the English catalog to
reproduce `SystemEntryMarkupBuilder.Default` exactly. So the samples must set
`CallerId`/`CallerName`, and the fallback wording must keep the caller as a
mention.

One shape detail: `Entries(AuthorId?)` is called twice, once with `null`, to
cover entries whose target author is absent. `CallEntry` has no such variant —
`CallerId` is non-nullable — so both batches give it the same caller, and the
`null` batch simply yields a duplicate sample. Harmless, and it keeps the
samples honest about what the type can hold.

## Compatibility and rollout

This is the only genuinely risky part of the change, because it touches two
contracts that older code reads.

**Old clients.** `ChatEntry` is a MessagePack union and the new kind takes tag
3. A client build that predates the change hits an unknown union tag while
deserializing, and the failure is not scoped to the one entry — it takes down
whatever payload carried it, i.e. a tile of the chat. Voxt ships MAUI builds
that live in the wild for a while, so this matters.

**Old servers.** As described above, a server without the `Call` option throws
`StandardError.Internal` when it reads such a row. That window opens during a
rolling deploy and, more importantly, during a rollback.

Rollout in two stages:

1. **Contract only.** Ship the type, both `[Union]` registrations, the legacy
   option, the diff fields and the render path — but emit nothing. Every client
   and server that has this build can read a `CallEntry`; none writes one.
2. **Enable emission.** `StreamingSettings`
   (`src/dotnet/Streaming.Service/Module/StreamingSettings.cs`) gains
   `bool CallEntriesEnabled { get; set; }`, default `false`, checked at the emit
   site in `CloseAndMaterialize`. It is flipped on once client adoption of the
   stage-1 build is high enough. A plain setting is the right tool here rather
   than a `FeatureDef` — nothing client-side needs to query it.

The setting is removed once stage 2 has been on in production long enough that a
rollback past stage 1 is not a consideration.

**Verify first.** The precise failure mode of an old client on an unknown
`ChatEntry` union tag is asserted here from how MessagePack unions work, not
from an experiment. The first implementation task is to confirm it against a
real pre-change client. If the failure is milder than described — say, the entry
alone degrades — stage 2 collapses into stage 1 and the setting is unnecessary.

## Reuse

**Existing abstractions this builds on:**

- `SystemEntry` / `ChatEntryDiff` / `ChatsBackend_ChangeEntry` / `Bots.GetWalleId`
  — the write path, exactly as `ChatsBackend.cs:2031` writes member changes.
- `LegacySystemEntry` — the on-disk wrapper; extended, not replaced.
- `SystemEntryMarkupBuilder` / `LocalizedSystemEntryMarkupBuilder` /
  `IStringLocalizer` + `Strings.*.json` — text.
- `AuthorCircleGroup` and the `c-live-card` styles in `conversation.css` — the
  card's avatars and chrome.
- `LiveSessionUI.StartCall` — the call-back action.
- `LiveSessionsBackend._changeLocks` and the existing `CloseAndMaterialize`
  funnel — ordering and exactly-once emission.
- `SystemEntryLocalizationTest` — already guards new union kinds; it needs new
  samples, not new machinery.

**New components and where they belong:**

- `CallEntry` / `CallOutcome` → `ActualChat.Api` (`src/dotnet/Api/Chat/`), beside
  the other entry kinds. They are shared by server and client by construction;
  no more-shared home applies.
- `LegacyCallOption` → `src/dotnet/Api/Chat/LegacySystemEntry.cs`, beside its
  siblings.
- `CallMessageView` → `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Call/`.
  This one is genuinely specific to the chat message list — it depends on
  `ChatContext`, the message-list item model and `LiveSessionUI`. Promoting it to
  a shared project would drag those along, so local placement is right. Its
  outcome-to-wording mapping is a static helper inside it; if group calls later
  need the same mapping server-side, that helper moves to `CallEntry` itself.

## Testing

- **Unit, outcome selection.** `LiveSessionsBackend`: decline-then-cancel keeps
  `Declined`; cancel-then-decline keeps `Canceled`; ring expiry gives `NoAnswer`.
- **Unit, emission gate.** No entry when the call connected
  (`SessionStartedAt is not null`), when `Outcome` is `None`, when the chat is
  not a peer chat, or when `CallEntriesEnabled` is off.
- **Integration, round trip.** A call that goes unanswered writes exactly one
  `CallEntry` into the peer chat; reading it back through `IChats` yields a
  `CallEntry` with the caller, invitee and video flag intact. This is also the
  regression test for the legacy JSON wrapper.
- **Unit, localization.** Extend `SystemEntryLocalizationTest.Entries()` with a
  `CallEntry` per outcome. The four existing facts then cover the fallback text
  across every shipped language for free.
- **Unit, per-side wording.** The outcome-to-wording mapping, driven as a theory
  over (outcome × is-caller), asserting the six cells of the table.

## Open questions

1. The wording table is an interpretation of the mockup and should be confirmed
   before the strings are translated into 20-odd languages.
2. Whether stage 2 (the `CallEntriesEnabled` setting) is needed depends on the
   old-client experiment described in Compatibility.
