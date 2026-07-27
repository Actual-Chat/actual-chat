# VideoLoadTest (TypeScript)

TypeScript port of `src/dotnet/App.VideoLoadTest/Program.cs`. Measures end-to-end
push/pull latency and throughput of the Fusion RPC client so the numbers can be
compared directly against the C# harness results.

## What it does

1. Signs in as `test-videoload@actual.chat` with dev OTP `111111` via the Fusion
   RPC `IEmailAuth.OnValidateTotp` command (same call path as the C# test).
2. Starts `chats × streamsPerChat` producers — each opens one WebSocket and
   pushes synthetic frames at 30 fps (40 KB keyframes / 10 KB deltas, GOP 30).
3. Discovers the live streams per chat via `ILiveVideoStreams.List`.
4. Starts `chats × consumersPerChat × (streamsPerChat - 1)` consumers; consumer
   `N` skips stream `N` in its chat.
5. Runs for `durationSec`, then prints a per-chat + aggregate report with
   frames received, throughput (MB/s), and latency percentiles (p50/p95/p99).

Frame layout, GOP size, data sizes, chat IDs, and report format all match the
C# harness exactly.

## Prerequisites

- The server is running locally with the dev OTP bypass (OTP `111111` valid for
  any `test-*@actual.chat` email).
- The 10 chat IDs hard-coded in `index.ts` (copied verbatim from
  `src/dotnet/App.VideoLoadTest/Program.cs`) exist locally.

## Run

```bash
# From repo root
npm install                                      # pulls in tsx, ws, @types/ws, @types/node
npm run test:video-load                          # 10 chats × 6 × 6, 30s
npm run test:video-load -- -c:5 -s:3 -n:3 -d:15  # smaller/faster
```

### Dev-cert TLS bypass

`local.voxt.ai` runs with a self-signed / mkcert cert that Node's default CA
bundle does not trust. The harness automatically sets
`NODE_TLS_REJECT_UNAUTHORIZED=0` when the base URL matches a dev host
(`local.voxt.ai`, `localhost`, `127.0.0.1`) and injects
`rejectUnauthorized: false` into the Fusion RPC `ws` connection (`node-ws.ts`).
You will see this line on every run:

```
[load-test] NODE_TLS_REJECT_UNAUTHORIZED=0 — dev cert bypass active
```

This is intentional and dev-only. The bypass is gated on the base URL —
point `-u:` at anything else and TLS verification works normally. Never
import any of these files into production code.

CLI flags (mirror C# harness):

| Flag | Default | Meaning |
|------|---------|---------|
| `-c:N` / `-chats:N` | `10` | Number of chats (max = hard-coded chat ID count) |
| `-s:N` / `-streams:N` | `6` | Producers per chat |
| `-n:N` / `-consumers:N` | `6` | Consumers per chat |
| `-u:URL` / `-url:URL` | `https://local.voxt.ai` | Server base URL |
| `-d:SEC` / `-duration:SEC` | `30` | Test duration in seconds |

Ctrl+C stops early and prints the report from whatever data was collected.

## How it compares to the C# harness

| Aspect | C# `App.VideoLoadTest` | TS `app.video-load-test` |
|---|---|---|
| Sign-in | `ICommander.Call(EmailAuth_ValidateTotp)` | Direct RPC call to `IEmailAuth.OnValidateTotp` |
| Session header | Via .NET `Session` DI resolution | Via `Session` HTTP header on the `ws` WebSocket upgrade |
| RPC push | `ILiveVideoStreams.PushStream` + `RpcStream.New(IAsyncEnumerable)` | `ILiveVideoStreams.PushStream` + `RpcClientStreamSender<VideoFrameDto>` |
| RPC pull | `ILiveVideoStreams.GetStream` | `ILiveVideoStreams.GetStream` |
| Discovery | `Computed.Capture(List)` + `WhenInvalidated` | Polling `ILiveVideoStreams.List` every 500 ms until complete |
| Latency clock | `Stopwatch.GetTimestamp` | `Date.now()` (~1 ms resolution) |

Latency pairing uses `(chatIdx, producerIdx, offsetTicks)` for both harnesses.
The TS test tags each frame's send time at `Metrics.recordSent`; the consumer
reads the frame's `Offset` field to look up the matching send timestamp.

## Known differences to be aware of when reading numbers

- **Single-threaded event loop**: Node's task scheduling is cooperative. At
  high concurrency (10 chats × 6 streams × 6 consumers = 300 pulls + 60 pushes)
  the `setTimeout`-based pacing in `frame-gen.ts` may drift a few ms. The C#
  test uses `Task.Delay` which has the same OS-timer resolution floor.
- **`Date.now()` resolution**: 1 ms. C# uses `Stopwatch` (~100 ns). For latency
  distributions that span tens of ms this is irrelevant, but p50 values below
  1 ms will read `0` in TS.

## File map

- `index.ts` — CLI, orchestration, report
- `auth.ts` — RPC sign-in flow (IEmailAuth)
- `service-defs.ts` — TS `defineRpcService` declarations mirroring the .NET contracts
- `frame-gen.ts` — synthetic frame generator (PascalCase keys)
- `metrics.ts` — latency/throughput aggregation + percentile report
- `rpc-runner.ts` — Fusion RPC producer + consumer + stream discovery
- `node-ws.ts` — adapter that wraps `ws.WebSocket` into the `WebSocketLike`
  interface expected by `RpcClientPeer`, with `Session` header injection
- `tsconfig.json` — standalone TS project (Node types, ES2022)
