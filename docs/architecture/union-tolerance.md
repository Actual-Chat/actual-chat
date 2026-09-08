# Forward-compatible unions

Adding a member to a `[Union]` hierarchy used to be one of the riskier changes in this
codebase. MessagePack writes a union as `[tag, payload]`, and a reader that has no member
for the tag cannot tell how long the payload is — so it does not fail on that one value,
it fails on everything around it. For a `ChatEntry` that meant losing a whole tile of a
chat, not one message. This page describes how a union root survives meeting a member it
does not know, and how the server keeps the values it cannot make survivable away from the
peers that would break on them.

**The short version.** A union root that implements
`IForwardCompatibleUnion<TSelf>` is registered with `ForwardCompatibleUnionFormatter<TBase>`
instead of MessagePack's own union formatter. The formatter reads the tag from a *copy* of
the reader; a known tag is handed to the standard formatter untouched, and an unknown one
goes to the root's `NewUnsupported`, which returns a placeholder. The surrounding payload
reads to completion. Peers old enough to predate the formatter cannot be fixed after the
fact, so `IChats` carries filtering twins that never send them a kind they cannot read.

## The formatter

`src/dotnet/Core/Serialization/Internal/ForwardCompatibleUnionFormatter.cs`

Deserialization peeks rather than consumes: the tag is read from a copy of the
`MessagePackReader`, so when the tag turns out to be known the real reader is still
positioned at the start of the envelope and the standard union formatter runs against
untouched input. Nothing about the known path changes — same bytes, same allocations, same
behaviour on malformed input.

Only the unknown path is new. The root's own `NewUnsupported` decides what an unreadable
member becomes:

```csharp
static abstract TSelf? NewUnsupported(
    int tag,
    ref MessagePackReader payload,
    MessagePackSerializerOptions options);
```

The reader handed to it is positioned at the unknown member's payload, and reading from it
is optional — the caller skips the whole envelope afterwards either way. That is what lets
a root recover the part of the value it does still understand. `ChatEntry` reads the base
prefix its members all share — `Id`, `Version`, `Flags`, `AuthorId` — so an entry from a
newer release keeps its identity and its place in the tile, and only its content is
replaced.

## Which roots tolerate, and which are exempt

Tolerant today: `ChatEntry`, `SystemEntry`, `Markup`, `Notification`, `Invite`.

`tests/Chat.UnitTests/UnionToleranceCoverageTest.cs` fails any `[Union]` root that is
neither tolerant nor listed in its `Exempt` table with a reason, so a new hierarchy cannot
quietly opt out. Three roots are exempt, and the reasons are worth knowing because they
mark the boundary of what tolerance buys:

| Root | Why not |
|---|---|
| `StoredSettings` | Tolerance alone would not fix its bug — an unreadable row is dropped on write-back, which is a change to the settings write path, not the reader. |
| `MuxedAudioStreamItem` | No placeholder means less than a throw: the demuxer keys off `StreamIndex`, so a null in that position is a *different* failure, not a lesser one. |
| `MediaFrame` | Same shape — a frame the pipeline cannot decode is not a frame it can carry as null past timestamping and A/V sync. |

The test also checks the inverse — that every root registered as tolerant knows its own
tags — so a formatter registration cannot drift away from the hierarchy it serves.

## The system-entry range

`ChatEntry` splits its tag space, and the split is load-bearing rather than tidy:
`ChatEntry.IsSystemUnionTag` treats **100..199** as system entries — plus the two tags
`LegacySystemUnionTags` names, which predate the range and cannot be moved — which is how
the formatter knows to build an `UnsupportedSystemEntry` rather than a plain unsupported
entry for a tag it has never seen. A new system entry that takes a tag outside that range is
still readable, but it degrades to the wrong placeholder.

## Peers that predate the formatter

Tolerance only helps a peer that has the formatter. Anything at or below
`ApiConstants.LastVersionWithoutUnionTolerance` (`2.18.9999`) still dies on an unknown tag,
and no server-side change can alter that — so the server does not send them one.

Three entry points carry a `ChatEntry` to a client, each selected by the peer's handshake
version through `[LegacyName]` version bands:

| Method | Serves | Filtering |
|---|---|---|
| `IChats.GetTile` → `GetLegacyTile` | ≤ 2.18.9999 | Drops entries whose kind postdates the peer's API version. |
| `IChats.GetNews` → `GetLegacyNews` | 2.13 … 2.18.9999 | Substitutes the previous readable entry for an unreadable `LastTextEntry`. |
| `IChats.GetNews` → `GetFullNews` | ≤ 2.12.9999 | The same, through the same backend: the older band is also a pre-2.19 band. |

The band split on `GetNews` is a leftover from an earlier migration — `GetFullNews` returned
the full entry where `GetNews` returns a slim one — and the resolver picks the lower band on
a name collision. It is easy to fix only one of the two and leave the *oldest* clients less
protected than the newer ones, which is exactly the shape of the bug that once shipped here.

`ChatEntry.UnionTagSinceVersions` (`src/dotnet/Api/Chat/ChatEntry.Unsupported.cs`) is the
table both twins read: tag → the release that introduced it. **An undeclared tag is treated
as known to everyone**, so omitting a row does not fail safe — it sends the new kind to
exactly the peers it breaks. Declaring the row is the whole of what a new union member owes
this mechanism.

The product consequence, stated plainly: a pre-2.19 client sees **nothing** where a newer
entry is — a gap in the tile's lid range, the same shape a removed entry already produces —
rather than a placeholder.

### Why `GetNews` substitutes rather than nulls

`ChatNews.LastTextEntry` is not only the chat list's preview line; it is its primary sort
key, which orders by `LastTextEntry?.Version ?? Contact.Version`
(`src/dotnet/UI.Blazor.App/Services/ChatListExt.cs`). Nulling it would blank the line *and*
move the chat up or down the list. Serving the previous readable entry instead leaves an old
client's list exactly as it stood — same line, same position; it simply never learns the
newer entry happened, which is the most it can be told.

The fallback is cheap to keep correct because `ChatsBackend.GetNews` treats the last entry
as the final non-removed entry of the last tile and nothing more. Stepping back is stepping
back through the same array under the same predicate, so the two cannot drift apart. The
walk is bounded by `MaxLegacyNewsTiles`; past that the chat reads as one with no messages
rather than scanning its whole history on every chat-list render.

## Old servers

A server rolled back past a release that added a member meets the same unknown tag in the
database rather than on the wire. `DbChatEntry.ToModel`'s unknown-option arm yields the same
placeholder instead of throwing, so a rollback degrades rows rather than breaking every chat
that holds one.

## Design decisions

**Peek, don't consume.** Reading the tag from a copy costs one struct copy per union value
and keeps the known path byte-identical to the stock formatter. The alternative — read the
tag, then reconstruct an envelope for the standard formatter — would have made every
existing union value take a new code path to gain nothing.

**A placeholder per root, not one shared type.** What an unreadable value should become is a
property of the hierarchy: an entry keeps its identity and loses its content, a markup node
becomes text, a notification is dropped. Only the root knows which, so `NewUnsupported` is
`static abstract` on the root rather than a shared fallback.

**A table, not an attribute.** `UnionTagSinceVersions` lives next to the tags it describes
so the two are read together, and so the filtering twins have one place to consult rather
than a reflection pass over the hierarchy.

## Related

- [Serialization](./serialization.md) — which serializer owns which path, and the attribute
  conventions a union member must follow.
- [Command idempotency](./command-idempotency.md) — the other half of the rollout story: how
  a client-generated `Uuid` and a version-gated deserializer keep old clients working.
- [Call entries](../call-entries.md) — the first feature to take a tag in the system range,
  and a worked example of what declaring one costs.
