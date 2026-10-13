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

## Reading the numbers

Nothing charts the call metrics yet. The series are in Google Managed Prometheus (project
`actual-chat-app-prod`), queryable in Cloud Monitoring's Metrics Explorer with its PromQL tab, or
over HTTP the way the funnel report in docs-internal (`docs/arrival-funnel.md`) does. The queries
below are the panels a call dashboard needs, in the order a reader asks the questions; each is
pasted as is, with the window changed to taste. A boolean tag arrives as the strings `true` and
`false`, so it is `has_video="true"`, not `has_video=true`.

**How many calls, and did they connect**

```promql
# Calls placed per hour, by chat kind and video
sum by (chat_kind, has_video) (increase(calls_started_total[1h]))

# Answer rate: the share of ended calls that were answered
sum(increase(calls_ended_total{outcome="Ended"}[1d]))
  / sum(increase(calls_ended_total[1d]))

# Ended calls by outcome
sum by (outcome) (increase(calls_ended_total[1d]))

# Refused starts and accepts; a rise in chat_busy or accept_refused is two people racing the same chat
sum by (reason) (increase(calls_start_refused_total[1d]))
sum(increase(calls_accept_refused_total[1d]))
```

**Where answered calls failed**

```promql
# Answered calls by the reason they ended; HungUp and PartyLeft are normal, the rest are failures
sum by (reason) (increase(calls_ended_total{outcome="Ended"}[1d]))

# Failure share: answered calls that never connected or lost a client
sum(increase(calls_ended_total{reason=~"ConnectTimeout|Lost"}[1d]))
  / sum(increase(calls_ended_total{outcome="Ended"}[1d]))
```

**Did the ring arrive, and how fast**

```promql
# Ring delivery: the share of invites whose client confirmed the ring at all
sum(increase(calls_invites_total{ack!="None"}[1d]))
  / sum(increase(calls_invites_total[1d]))

# Invites by their final status and ack; Missed with ack None is a push that never woke the client
sum by (status, ack) (increase(calls_invites_total[1d]))

# Ring delivery delay, p50 and p95 (seconds from the ring's write to the client's confirmation)
histogram_quantile(0.5, sum by (le) (rate(calls_ring_delivery_delay_seconds_bucket[1h])))
histogram_quantile(0.95, sum by (le) (rate(calls_ring_delivery_delay_seconds_bucket[1h])))

# Answer delay, p50 and p95 (seconds from StartCall to the first answer)
histogram_quantile(0.5, sum by (le) (rate(calls_answer_delay_seconds_bucket[1h])))
histogram_quantile(0.95, sum by (le) (rate(calls_answer_delay_seconds_bucket[1h])))

# How long unanswered calls dialed before giving up, by outcome
histogram_quantile(0.5, sum by (le, outcome) (rate(calls_dial_duration_seconds_bucket[1d])))
```

**How long and how big**

```promql
# Talk time: median per chat kind, and the day's total in minutes
histogram_quantile(0.5, sum by (le, chat_kind) (rate(calls_duration_seconds_bucket[1d])))
sum(increase(calls_duration_seconds_sum[1d])) / 60

# Calls that ended within 15 s of connecting: a connected call nobody could hear
sum(increase(calls_duration_seconds_bucket{le="15"}[1d]))
  / sum(increase(calls_duration_seconds_count[1d]))

# Parties per answered call: the average, and the share with more than two
sum(increase(calls_party_count_sum[1d])) / sum(increase(calls_party_count_count[1d]))
1 - sum(increase(calls_party_count_bucket{le="2"}[1d])) / sum(increase(calls_party_count_count[1d]))
```

**Alerts.** Two conditions are worth paging on. Both require a minimum number of calls in the
window, or a single bad call at night fires them.

```promql
# More than a fifth of answered calls failed over the last hour, with at least ten to judge by
(
  sum(increase(calls_ended_total{reason=~"ConnectTimeout|Lost"}[1h]))
    / sum(increase(calls_ended_total{outcome="Ended"}[1h]))
) > 0.2
and sum(increase(calls_ended_total{outcome="Ended"}[1h])) >= 10

# Fewer than half the rings were confirmed over the last hour: the push path is down
(
  sum(increase(calls_invites_total{ack!="None"}[1h]))
    / sum(increase(calls_invites_total[1h]))
) < 0.5
and sum(increase(calls_invites_total[1h])) >= 10
```

A histogram's `_sum` and `_count` series are exported next to its `_bucket`s, which is what the
averages and totals above read. The quantile queries need the fixed bucket bounds `CallMeters`
sets: a ring is 20 s plus a 10 s grace, so the ring histograms have half-second to a minute
bounds, and the duration one runs from 5 s to two hours; the OpenTelemetry defaults (0, 5, 10, 25 ...)
would put nearly every ring in one bucket.

## Checking a change

- `CallMetricsTest` (`tests/Chat.IntegrationTests`) drives the backend through a canceled, a
  declined, an answered and a refused call with a `MeterListener` on the `calls.*` instruments.
- Locally, with the server running, watch the instruments live:
  `dotnet-counters collect -p <server pid> --counters Streaming --refresh-interval 1 --format csv -o tmp/counters.csv`
  (install once with `dotnet tool install -g dotnet-counters`).
- In production, run any query from [Reading the numbers](#reading-the-numbers) against Managed
  Prometheus; the series exist from the first deploy that carries `CallMeters`.
