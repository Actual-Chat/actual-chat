# Call metrics

What the server records about every call, where the numbers land, and how to read them. The ring
itself is described in [Incoming call flow](./incoming-call-flow.md), and the chat entry a call
leaves in [Call entries](./call-entries.md); this page is about the numbers that outlive both.

**The short version.** `CallMeters` (`src/dotnet/Streaming.Service/Diagnostics/`) counts calls as
`LiveSessionsBackend` places, refuses and ends them, and every ended call writes one log line with
the numbers the meters aggregate away. The meters go out with the rest of the server's OTLP metrics
to Google Managed Prometheus; the log line goes to Cloud Logging. Nothing runs on a client: a
counter created in WASM or MAUI is never exported.

## The instruments

All on the `Streaming` meter, so the names below are what PromQL sees after the collector's
renaming (`.` → `_`, `_total` on a counter, the unit and `_bucket` on a histogram).

| Instrument | PromQL name | Recorded when | Tags |
|---|---|---|---|
| `calls.started` | `calls_started_total` | `StartCall` placed a new call or joined the connected one | `chat_kind`, `has_video`, `kind` = `placed` \| `joined` |
| `calls.start_refused` | `calls_start_refused_total` | `StartCall` threw | `reason` = `caller_busy` \| `chat_busy` \| `just_ended` |
| `calls.accept_refused` | `calls_accept_refused_total` | `AcceptCall` threw: the ring was over, or another call's | — |
| `calls.ended` | `calls_ended_total` | `EndCall` dropped the call's record | `outcome`, `reason`, `chat_kind`, `has_video` |
| `calls.invites` | `calls_invites_total` | the same, once per invite of the call | `status` (final `CallInviteStatus`), `ack` (the `RingAck` the client sent, or `None`) |
| `calls.ring_delivery_delay` | `calls_ring_delivery_delay_seconds_bucket` | per invite whose client confirmed the ring | — |
| `calls.answer_delay` | `calls_answer_delay_seconds_bucket` | per answered call: `StartCall` to the first answer | — |
| `calls.dial_duration` | `calls_dial_duration_seconds_bucket` | per unanswered call: `StartCall` to its end | `outcome` |
| `calls.duration` | `calls_duration_seconds_bucket` | per answered call: the answer to the end | `chat_kind`, `has_video` |
| `calls.party_count` | `calls_party_count_bucket` | per answered call: the caller plus everyone who accepted | `chat_kind` |

`outcome` is the `CallOutcome` the call's entry gets: `Ended` for an answered call, otherwise
`NoAnswer`, `Declined`, `Canceled` or `Busy` as recorded during the ring (see
[Call entries → Write path](./call-entries.md#write-path)). `reason` is a `CallEndReason`, the
server's side of the story: which request or check closed the call.

| `reason` | Closed by |
|---|---|
| `Canceled` | the caller's `CancelCall` while the call was dialing |
| `Declined` | a `DeclineCall` that left nobody ringing |
| `NoAnswer` | `ExpireRings`: the ring timed out |
| `Busy` | `CloseBusyCall`: every invitee was in another call |
| `HungUp` | the last party of an answered call left it |
| `PartyLeft` | a party left and nobody came back within `CallLeaveGrace` |
| `ConnectTimeout` | an answer whose parties never got their presence up within `CallConnectGrace` |
| `Lost` | `GetCall` found a party's presence lapsed: a crashed or offline client |
| `SessionClosed` | the session the call ran in closed first |

The two tags together separate the questions "how did it go" from "where did it fail". A
`Ended`/`ConnectTimeout` is an accepted call that never connected; a `Ended`/`Lost` is one that
connected and then lost a client.

**Tags stay bounded.** Enum names and fixed words only, never an id. `chat_kind` is the `ChatKind`
of the chat the call was placed in. `has_video` is how the call was placed, not whether a camera
was turned on later.

**What isn't counted.** The checks the public facade makes before the backend sees a call: a
caller who isn't a member, or the peer-chat `CanWriteAudio` gate. Anything only a client knows:
a denied microphone before the ring, the offline watchdog in `CallUI`, a join that failed after
the accept, CallKit action failures. Those reach the server only as logs, if at all.

## The log line

`EndCall` writes one `Information` line per call, from the same values the meters get:

```
Call #<callId> ended: Ended (PartyLeft), Peer chat, video=False, invitees=1, parties=2, answered in 4.2s, lasted 3m 12s
```

That is where a user's report starts: the chat id is the call id's prefix, and the line names the
outcome, the reason and every delay. `StartCall` logs the call's placement with the ringing and busy
counts, and the grace checks log their verdicts between the two.

## Checking a change

- `CallMetricsTest` (`tests/Chat.IntegrationTests`) drives the backend through a canceled, a
  declined, an answered and a refused call with a `MeterListener` on the `calls.*` instruments.
- Locally, with the server running, watch the instruments live:
  `dotnet-counters collect -p <server pid> --counters Streaming --refresh-interval 1 --format csv -o tmp/counters.csv`
  (install once with `dotnet tool install -g dotnet-counters`).
- In production, query Managed Prometheus the way the funnel report does, for example
  `sum by (outcome, reason) (increase(calls_ended_total[7d]))`.
