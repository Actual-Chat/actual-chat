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
(`src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs:1132`) short-circuits
for calls: it dismisses lingering rings and drops the session, materializing
nothing.

So after a missed call the chat looks exactly as it did before the call. There
is nothing to tap to call back, and nothing to tell the callee that someone
tried to reach them.

## Scope

In scope:

- Four outcomes: **no answer** (ring expired), **declined** (invitee refused),
  **canceled** (caller hung up before anyone answered), and **ended** (the call
  connected and finished).
- Peer chats only. The model carries invitees so group calls can be enabled
  later without a data migration.
- Per-side wording: the caller and the callee read different text off the same
  stored entry.
- The entry behaves like an ordinary system entry for unread/chat-list purposes
  — the same as "X has left the chat". No dedicated push notification.

`Ended` earns its place now rather than later: a call with transcription off
currently leaves **nothing** in the chat — no transcript entries, and no
materialized conversation, because `CloseAndMaterialize` short-circuits for
calls. Such a call is invisible after the fact.

Out of scope (deliberately, and each is additive later):

- Group-call entries, where several invitees resolve differently.
- The live "Incoming call" / "Ongoing call" cards from the mockup. Those are
  live-session state and are already what the live conversation block renders:
  once a call is answered the session latches, `SessionStartedAt` is set, and
  `LiveSessionState.ToConversation()` surfaces it as the live card — with
  `Conversation_VoiceOnly` when transcription is off.

## User-facing behaviour

A failed call and a finished one look different in the chat, and they are drawn
by different components. This split is the single most important shape decision
in the design, so it is stated first.

### The three failed outcomes: their own card

No call took place, so there is no conversation to show — only the fact of the
attempt. These render as a small card in the message list, in the same visual
family as the live conversation card (`c-live-card` in
`src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/conversation.css`):
an icon and a title on the first line, an optional hint on the right, and a meta
row with avatars and the time.

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

Avatars come from `CallerId` plus `InviteeIds` — the people who were *rung*.
There are no participants to show, because nobody joined.

### `Ended`: the conversation card, in call mode

A finished call keeps everything the conversation card already does — the title,
the summary, the meta row, and **expanding to reveal the conversation's
messages** — and gains call chrome on top: the phone icon, "Call ended", and the
duration. It is therefore drawn by the existing conversation item
(`ConversationMessageView` and its header/footer), not by a second component.
Re-implementing expansion, summaries and attachments in a bespoke card would
duplicate the most intricate view in the chat.

That requires two things:

- A connected call must **materialize its conversation**, which it does not do
  today. See Write path.
- The conversation must know it was a call, so the item can pick call chrome:
  `Conversation.IsCall`.

Everything the call chrome needs is already on `Conversation`: duration is
`EndsAt - StartsAt`, and the avatars are `AuthorIds` — the people who actually
took part, which for a finished call is the right set and differs from the
invitees.

The `CallEntry` with `Outcome = Ended` is therefore **not rendered in the message
list** at all; it is skipped where `ChatUI.Tiles` builds its messages, beside the
existing skip for an unsupported system event
(`src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs:1206`). Two cards for one
call is the failure mode being avoided.

It is still written, and not merely for uniformity — see Write path for why it is
structurally required.

Both render paths take their icon and wording from one static mapping, so the
outcome table above cannot drift between them.

### Server-composed text

The chat-list last-message preview, notifications and search indexing have no
reader, so they use a neutral third-person rendering produced by
`SystemEntryMarkupBuilder` (see Localization).

One consequence, accepted deliberately: `ChatNews.LastTextEntry` is a
`ChatEntry?` and does not exclude system entries, so after a **transcribed** call
the chat-list preview changes from the last spoken line to the call line. This
matches what Telegram and WhatsApp show for a call, and it is the same behaviour
for all four outcomes.

## Data model

New file `src/dotnet/Api/Chat/CallEntry.cs`:

```csharp
public enum CallOutcome {
    None = 0,
    NoAnswer = 1,
    Declined = 2,
    Canceled = 3,
    Ended = 4,
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
mockup. `InviteeIds` is the group-call seat, and doubles as the avatar list for
the three failed outcomes. `HasVideo` earns its place now: the call-back action
needs to know which kind of call to place.

One field deliberately absent: a link to the conversation. `Ended` is not
rendered from the entry, so nothing reads a conversation through it — the
conversation item finds its own conversation the way it always has, and the entry
only has to be skippable. Nor is a duration field needed: `EndsAt - StartsAt` on
the conversation is the duration, and the neutral preview text does not quote it.

The enum is why this is one type rather than a record per outcome. The precedent
is next door — `MembersChangedEntry` models "joined" and "left" as one type with
a `HasLeft` field, not as two union members — and the cost of the alternative is
concrete: each new outcome would burn a union tag, need its own
`UnionTagSinceVersions` row, its own arm in `SystemEntryMarkupBuilder`, its own
option in the database envelope, and would vanish entirely for pre-2.19 peers.
An added enum value costs none of that.

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
`TargetAuthorId = null` (`ChatsBackend.cs:2031`), and the name is then the only
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

- `src/dotnet/Api/Chat/ChatEntry.cs:20` — add `[Union(101, typeof(CallEntry))]`.
  The concrete kinds are declared on `ChatEntry`, not only on `SystemEntry`, and
  **101, not 3**: tags 100..199 are the `SystemEntry` range that
  `ChatEntry.IsSystemUnionTag` reads, and a system entry landing outside it would
  be classified as a message when an older peer meets it as an unknown tag.
  `UnsupportedSystemEntry` holds 100.
- `src/dotnet/Api/Chat/SystemEntry.cs:12` — add `[Union(3, typeof(CallEntry))]`.
  This is a separate tag space from the one above; here 0..2 are taken.
- `src/dotnet/Api/Chat/ChatEntry.Unsupported.cs` — add
  `[101] = new (2, 19)` to `UnionTagSinceVersions`, naming the release
  `CallEntry` actually ships in. A guard test on the tolerance branch fails any
  member added after tolerance that does not declare its release, so this is not
  optional. See Compatibility for what the table drives.
- `src/dotnet/Api/Chat/ChatEntry.cs:216` — `ChatEntryKind.Call = 3`, plus the
  `NewEmpty` and `ChatEntryDiff(ChatEntry)` switches at lines 40 and 178. This
  enum is model-side only and unrelated to the union tags above.
- `src/dotnet/Api/Chat/ChatEntry.cs:173` — `ChatEntryDiff` gains
  `CallerId`, `CallerName`, `Outcome`, `InviteeIds`, `HasVideo`, all nullable.
  The diff is applied by `DiffEngine.DynamicPatch`
  (`src/dotnet/Chat.Service/ChatsBackend.cs:1450`), which matches **by property
  name**, so these names must equal the entry's exactly. Reusing the existing
  `TargetAuthorId`/`TargetAuthorName` pair would not map.

`DbChatEntry.Kind` stays 0 — it is a legacy column, always written as 0
(`src/dotnet/Chat.Service/Db/DbChatEntry.cs:194`), unrelated to
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
  (line 109) and `DbChatEntry.ToLegacySystemEntry` (line 254).

Two alternatives were considered and rejected. Writing the payload as its own
JSON straight into `Content`, bypassing the envelope, leaves nothing to tell the
two shapes apart on read short of sniffing the JSON. Pressing the existing `Kind`
column into service as the discriminator looks tempting — it is an `int` that is
always written as 0 — but it sits in the unique index `(ChatId, Kind, LocalId)`
and in the lid-range and `GetMaxLid` queries, which all assume 0; a non-zero
value on some rows would break those ranges.

`LegacySystemEntry.From` (line 34) duplicates `DbChatEntry.ToLegacySystemEntry`
and has no callers today. Two conversions that must agree, one of which nothing
exercises, is how the next kind ends up half-registered — so they collapse into
one: `From` gains the `CallEntry` arm and `ToLegacySystemEntry` becomes a call to
it. The public one is also the testable one, which is what gives the round trip
through the envelope a unit test rather than only a database test.

A server that does not know the `Call` property deserializes such a row into
`Option == null`. That used to throw and take out every chat holding one; since
the tolerance work it falls to `_ => new UnsupportedSystemEntry(id, Version)`
(`DbChatEntry.cs:121`) and the row reads back as the same placeholder the wire
format's unknown tags produce. Nothing further is needed on the database side.

## Write path

The outcome is decided in three places and written in one.

**Decide.** `LiveSessionState` (`src/dotnet/Api/Live/LiveSessionState.cs`) gains
two fields, appended after `IsExpandedByDefault`, which currently holds 21:

- `[DataMember(Order = 22), Key(22)] CallOutcome Outcome`
- `[DataMember(Order = 23), Key(23)] bool HasVideo`

`HasVideo` has to be stored because `StartCall` receives it as an argument
(`LiveSessionsBackend.cs:527`) and today only forwards it to
`NotificationsBackend_NotifyCall` — by close time it is gone, and the call-back
action needs it. `StartCall` sets it alongside the rest of the state.

This record lives only in Redis, so there is no migration; an old value simply
deserializes with `Outcome = None` and `HasVideo = false`.

Three call sites set it, each already holding the fact:

| Site | Sets |
| --- | --- |
| `DeclineCall` (`LiveSessionsBackend.cs:619`) | `Declined` |
| `CancelCall` (`LiveSessionsBackend.cs:646`) | `Canceled` |
| `ExpireRings` (`LiveSessionsBackend.cs:976`) | `NoAnswer` |

**First writer wins**: a site sets `Outcome` only while it is still `None`.
Without that rule a caller who hangs up a moment after the invitee declined
would overwrite `Declined` with `Canceled`, and the chat would report the wrong
story. All three sites already run under `_changeLocks.Lock(chatId)`, so the
check and the write are atomic.

**Emit.** The single write site is `CloseAndMaterialize`
(`LiveSessionsBackend.cs:1132`), in its existing `state.IsCall` branch, gated
overall by `state.ChatId.Kind == ChatKind.Peer` — the peer-only scope, and the
one line that opens group calls later.

Inside it the branch splits on whether the call ever connected, and the split is
decided by that fact, **not** by the `Outcome` field:

| `state.SessionStartedAt` | Entry written | Also |
| --- | --- | --- |
| `null` (never connected) | the recorded `Outcome`, when it is not `None` | — |
| set (connected) | `Ended` | materialize the conversation |

Deciding by `SessionStartedAt` rather than by `Outcome` closes a corner that
would otherwise bite: `CancelCall` is also how a caller hangs up a call that *did*
connect, and it would leave `Outcome = Canceled` behind. A call that happened is
`Ended` regardless of which button ended it. An ordinary hang-up arrives here the
same way, through `LeaveCall` once fewer than two participants remain.

It then calls `ChatsBackend_ChangeEntry` with `Change.Create(new ChatEntryDiff {
Kind = ChatEntryKind.Call, AuthorId = Bots.GetWalleId(chatId), ... })`, the same
shape `ChatsBackend.cs:2037` uses for member changes. Wall-E authors the entry;
the failed-outcome card draws its avatars from `CallerId`/`InviteeIds`, not from
the entry's author.

Emitting from `CloseAndMaterialize` rather than from the three deciding sites is
what makes the write exactly-once: `Close` removes the Redis state under the
same lock, so the state that carries a pending `Outcome` cannot be seen twice.
`CancelCall` reaches close through `CloseNow`, which returns early if the
session is still live — but it stops every ring before that, so it isn't.

**Materializing a connected call.** Two conditions currently prevent it, and both
have to move:

- The `state.IsCall` branch returns before reaching the materialize call at
  `LiveSessionsBackend.cs:1153`. A latched call must fall through to it.
- The general branch materializes only when `!state.Title.IsNullOrEmpty()`. A
  call with transcription off has no title by construction, so the gate has to
  admit calls — that is exactly the case the card exists for.

The conversation is `state.ToMaterializedConversation()` with `IsCall = true`.
`Conversation` (`src/dotnet/Api/Chat/Conversation.cs`) gains that field and
`DbConversation` the matching column — the same shape as the `IsExpandedByDefault`
pair beside it, so one additive migration.

**Why `Ended` must be written even though nothing renders it.** For a call with
transcription off, the conversation's lid range contains **no entries at all**.
`ChatUI.Tiles` builds its list from entries and hangs conversation headers and
footers around them, so a conversation spanning nothing has nothing to attach to
and would not appear. The `Ended` entry is the one entry inside that range — it
is what the conversation card is drawn around. It also supplies unread and the
chat-list line, which a conversation cannot: conversations are not entries.

`InviteeIds` comes from `SafeGetInvites(state.ChatId)`, which
`CloseAndMaterialize` already reads in this branch to dismiss lingering rings.

`CallerName` is resolved at emit time from the caller's author, the same way
`ChatsBackend.cs:2032` resolves a member name, with `MentionMarkup.NotAvailableName`
as the fallback. See the note under Data model for what it is actually for — it
is not the name normally displayed.

## Render path

Two paths, per the split in User-facing behaviour.

**The three failed outcomes.** `ChatEntryMessageView.razor:70` currently sends
every `SystemEntry` down one branch that renders centered markup. A new branch
above it routes `CallEntry` to `CallMessageView`, a new component under
`src/dotnet/UI.Blazor.App/Components/ChatView/Items/Call/`.

`CallMessageView` is a `ComputedStateComponent` that resolves:

- whether the reader is the caller (`Chat.Rules.Author?.Id == entry.CallerId`),
- the title, the icon and the optional hint from the shared mapping,
- the avatars, via the existing `AuthorCircleGroup`
  (used the same way at `ConversationMessageView.razor:74`).

The `Tap to call back` action calls `LiveSessionUI.StartCall(chatId, [caller],
entry.HasVideo, cancellationToken)` — the signature at
`src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs:104`.

**`Ended`.** Skipped where the tile is built, next to the existing skip for an
unsupported system event (`src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs:1206`):

```csharp
if (e is CallEntry { Outcome: CallOutcome.Ended })
    continue;
```

The conversation item renders it instead. `ConversationMessageView` and its
header gain a call mode driven by `Conversation.IsCall`: the phone icon and
"Call ended" in place of the title when there is none, and the duration
(`EndsAt - StartsAt`) in the meta row. Everything else — expansion, summary,
attachments, the author circles — is untouched, which is the whole point of
routing through this component rather than a second card.

**Icons.** From the existing font (`src/nodejs/fonts/svgtofont/icon.css`), no new
glyphs: `icon-phone-missed` for a missed call, `icon-call-out` for an outgoing
one, `icon-phone-off` for declined and canceled, `icon-phone-call` for `Ended`.

**Shared mapping.** Outcome plus is-caller to icon, title and hint lives in one
static helper both paths call, so the table in User-facing behaviour has exactly
one implementation.

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
| `SystemEntry_CallEnded` | ` called.` |

`Ended` needs its own entry here even though the entry is never rendered in the
list: this is the text the chat-list preview shows after a call.

**Card text** — read through `IStringLocalizer` by `CallMessageView`
(`Call_Entry_Outgoing`, `Call_Entry_Missed`, `Call_Entry_Declined`,
`Call_Entry_Canceled`, `Call_Entry_NoAnswer`, `Call_Entry_TapToCallBack`) and by
the conversation item in call mode (`Call_Entry_Ended`).

All eleven keys go into every shipped `Strings.*.json` under
`src/dotnet/Localization/Resources/`, following `docs/i18n.md` — which means the
hand-written catalogs, then `scripts/derive-bcms.cmd` and `scripts/derive-max.cmd`
to regenerate the derived ones.

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

## Compatibility

Adding a `[Union]` member used to be the risky part of a change like this: a peer
that meets an unknown tag cannot tell how long the payload is, so the failure
takes down the whole tile rather than the one entry. That is no longer this
change's problem to solve. This branch sits on top of
`feat/forward-compatible-unions`, which made unknown tags survivable and is the
base commit under this work.

What is left is to use that machinery correctly, which is one table entry.

**Peers with tolerance (2.19 and later).** `ForwardCompatibleUnionFormatter`
reads the tag, classifies 101 as a system entry via
`ChatEntry.IsSystemUnionTag` — which is why the tag must sit in 100..199 — reads
the base prefix (`Id`, `Version`, `Flags`, `AuthorId`), skips the rest, and
yields an `UnsupportedSystemEntry` flagged `IsUnsupported`. The entry keeps its
identity and its place in the tile; only its content is replaced, by the
localized "update the app" line. Nothing breaks.

**Peers without it (at or below `ApiConstants.LastVersionWithoutUnionTolerance`,
`2.18.9999`).** They cannot be fixed retroactively, so the server does not send
them what they cannot read: `IChats.GetLegacyTile` drops entries whose
`UnionTagSinceVersions` release postdates the peer's API version, and the RPC
layer routes such peers there off the handshake version. This is why declaring
`[101] = new (2, 19)` is load-bearing rather than bookkeeping — an undeclared tag
is treated as known to everyone and would reach exactly the peers it breaks. The
guard test on the base branch fails a member that omits it.

The product consequence, stated plainly: a pre-2.19 client sees **nothing** where
a missed-call entry is — a gap in the tile's lid range, the same shape a removed
entry already produces — rather than a placeholder. That is the intended trade.

**One path the tolerance work did not cover.** It gave `IChats.GetTile` a
filtering twin but left `GetNews` alone, and `ChatNews.LastTextEntry` is a
`ChatEntry?` that `ToSlim` rebuilds with `entry with { … }` — preserving the
concrete type. So a `CallEntry` would reach a pre-2.19 client through the chat
list and take down the whole `ChatNews` payload. The gap is not created here —
it applies to `UnsupportedSystemEntry` as well, on a rollback — but this change
makes it routine rather than rare, because a `CallEntry` is the last entry of a
peer chat after every call. `GetNews` therefore gains the same twin, dropping a
last entry the peer cannot read.

Dropped rather than replaced with a stand-in: the preview line is composed on the
client, in the viewer's language, so anything the server substituted could only
be English. A chat whose last entry a peer cannot read reads as one with no
preview.

**Old servers.** Already handled: the unknown-option arm in `DbChatEntry.ToModel`
yields the same placeholder instead of throwing, so a rollback past this release
degrades rows rather than breaking chats.

No staged rollout and no feature flag. The version in the table is the only thing
to get right, and it must name the release `CallEntry` actually ships in — if the
branch slips a release, the number moves with it.

## Reuse

**Existing abstractions this builds on:**

- `SystemEntry` / `ChatEntryDiff` / `ChatsBackend_ChangeEntry` / `Bots.GetWalleId`
  — the write path, exactly as `ChatsBackend.cs:2036` writes member changes.
- `LegacySystemEntry` — the on-disk wrapper; extended, not replaced.
- The whole tolerance layer from `feat/forward-compatible-unions`:
  `ForwardCompatibleUnionFormatter`, `UnsupportedSystemEntry`,
  `ChatEntry.IsSystemUnionTag` / `UnionTagSinceVersions`, `IChats.GetLegacyTile`
  and the already-tolerant `DbChatEntry.ToModel`. `CallEntry` adds one row to a
  table and inherits every compatibility guarantee; nothing new is built here.
- `SystemEntryMarkupBuilder` / `LocalizedSystemEntryMarkupBuilder` /
  `IStringLocalizer` + `Strings.*.json` — text.
- `AuthorCircleGroup` and the `c-live-card` styles in `conversation.css` — the
  card's avatars and chrome.
- `ConversationMessageView` with its header and footer, plus
  `ConversationBackend_Materialize` and `LiveSessionState.ToMaterializedConversation`
  — the whole `Ended` card, expansion included. This is the largest piece of
  reuse in the design and the reason `Ended` is not its own component.
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
- `Conversation.IsCall` → the existing record and `DbConversation`, beside
  `IsExpandedByDefault`, with one additive EF migration. Not a new abstraction:
  a flag on a record that already carries the call's times and participants.
- `CallMessageView` → `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Call/`.
  Covers the three failed outcomes only. Genuinely specific to the chat message
  list — it depends on `ChatContext`, the message-list item model and
  `LiveSessionUI` — so local placement is right; promoting it to a shared project
  would drag those along.
- The outcome-to-wording mapping → its own static class in the same folder, not
  inside `CallMessageView`: the conversation item in call mode needs the same
  icon and title for `Ended`, and two copies of that table would drift. If group
  calls later need it server-side, it moves next to `CallEntry`.

## Testing

- **Unit, outcome selection.** `LiveSessionsBackend`: decline-then-cancel keeps
  `Declined`; cancel-then-decline keeps `Canceled`; ring expiry gives `NoAnswer`.
- **Unit, emission gate.** No entry when `Outcome` is `None` and the call never
  connected, or when the chat is not a peer chat.
- **Integration, the connected call.** A peer call that is answered and then hung
  up writes one `CallEntry` with `Outcome = Ended` and materializes a
  conversation with `IsCall = true` — including when transcription was off, which
  is the case both current gates reject. A caller who hangs up an *answered* call
  gets `Ended`, not `Canceled`: this is the corner the `SessionStartedAt` split
  exists for, so it gets its own test.
- **UI, no double card.** A tile containing an `Ended` entry and its conversation
  produces one card, not two — the entry is skipped in the tile builder.
- **Unit, legacy filtering.** A tile containing a `CallEntry` comes back without
  it through `GetLegacyTile` for a peer below 2.19, and with it at or above.
  `LegacyTileRoutingTest` on the base branch already covers the routing; this
  adds `CallEntry` to what it asserts.
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
2. The release named in `UnionTagSinceVersions[101]` — `2.19` assumes this lands
   alongside the tolerance work. It has to be corrected if the branch slips.
