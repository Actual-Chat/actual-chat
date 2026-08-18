# Dev chat-render delay — measured evidence (2026-08-17)

Handoff notes for whoever picks up the dev chat-render delay. Found while
debugging Android push-to-talk; **the recorder turned out to be innocent**, so
this is written up separately rather than fixed in that branch.

## One-line summary

On dev, a chat entry reaches the server within **0.76 s** of the speaker's voice
being detected, but takes **8.4 s** (Blazor Server) or **up to 60.9 s in one
batch** (WASM) to appear in another user's browser.

## How it was measured

Three clocks correlated, all UTC:

- **Phone** — `adb logcat -s dev.voxt.ai:V`, using the `VAD: Start` line as
  "the speaker's voice was detected".
- **Server** — `gcloud logging read` on `actual-chat-app-dev`, container
  `actual-chat-app`, using `PushStream: AudioRecord` as "the server has the
  stream".
- **Browser** — a `MutationObserver` on the chat-view virtual list
  (`.virtual-list.chat-view`), recording the wall-clock time each new
  `[data-key]` element is inserted, plus `document.visibilityState` at that
  moment and a log of every `visibilitychange`.

The observer matters: an earlier attempt used a 200 ms `setInterval`, and Chrome
throttles timers in hidden tabs to roughly once a minute — which fabricates
exactly the batch-arrival pattern being investigated. Every figure below comes
from the observer, on a tab that logged **no visibility transitions at all**.

## Result 1 — the upload leg is not the problem

Seven consecutive utterances, phone `VAD: Start` → server `PushStream`:

| VAD: Start | PushStream | Lag | server-reported `delta` |
|---|---|---|---|
| 17:33:57.691 | 17:33:58.454 | 0.76 s | 236 ms |
| 17:34:41.042 | 17:34:41.804 | 0.76 s | 236 ms |
| 17:34:56.004 | 17:34:56.775 | 0.77 s | 245 ms |
| 17:35:34.303 | 17:35:35.064 | 0.76 s | 233 ms |
| 17:36:08.136 | 17:36:08.899 | 0.76 s | 234 ms |
| 17:39:14.158 | 17:39:14.927 | 0.77 s | — |
| 17:39:28.358 | 17:39:29.124 | 0.77 s | — |

Streams end normally with real frame counts (665, 301, 292, 243 …), and Soniox
finds speech endpoints inside them. The audio is on the server, promptly, intact.

## Result 2 — WASM: a stall, then a batch flush

Render mode `'w'`. Tab **visible** throughout, zero visibility transitions.

| PushStream | DOM patched | Lag |
|---|---|---|
| 17:34:56.775 | 17:37:05.840 | **129 s** |
| 17:35:35.064 | 17:37:05.840 | **91 s** |
| 17:36:08.899 | 17:37:05.840 | **57 s** |
| 17:39:14.927 | 17:40:15.787 | **60.9 s** |
| 17:39:29.124 | 17:40:15.787 | **46.7 s** |

Two independent observations, both showing the same two properties:

1. **Entries appear simultaneously**, in one batch, however far apart the
   pushes were (the 17:34–17:36 group spans 72 s of pushes and lands in a single
   DOM patch).
2. **The shortest lag in each batch is ~57–61 s.**

A working invalidation would deliver per-entry, staggered like the pushes.
Batching plus a ~60 s floor is the signature of the view not being invalidated at
all, and only catching up when a periodic fallback fires. The 60 s dev fallback
on the invalidation-delivery path is the obvious suspect.

## Result 3 — Blazor Server: slow but not stalled

Render mode `'s'`, same tab, same chat, same session, tab visible throughout.

| PushStream | DOM patched | Lag |
|---|---|---|
| 17:46:59.287 | 17:47:08.003 | 8.7 s |
| 17:47:18.696 | 17:47:27.125 | 8.4 s |
| 17:47:25.664 | 17:47:34.115 | 8.5 s |
| 17:47:38.793 | 17:47:47.080 | 8.3 s |

Staggered per entry, consistent ~8.4 s. No batching, no 60 s floor.

**So the batch stall did not reproduce under Server rendering.** That points at
the WASM client's invalidation delivery rather than at the server's
invalidation *production* — consistent with Result 4.

Caveat: this is one round of four entries. It shows Server mode did not stall
here; it does not prove it never does.

## Result 4 — the server is quiet

Across every stall window, filtering the app container for `op-log`,
`OperationLog`, `Npgsql`, `fallback`, `invalidat`, `UpdateOnlineNodes`,
`Rerouting`, `watcher` returns **nothing**. No mesh churn, no rerouting, no
op-log watcher complaints. The server has the data and logs no trouble
publishing it.

## Reproducing

```js
// In the receiving browser, on the chat page:
const list = [...document.querySelectorAll('.virtual-list')]
  .find(l => l.className.includes('chat-view'));
window.__log = []; const seen = new Set();
const note = () => { for (const el of list.querySelectorAll('[data-key]')) {
  const k = el.getAttribute('data-key'); if (seen.has(k)) continue; seen.add(k);
  window.__log.push({ key: k, at: new Date().toISOString(), vis: document.visibilityState });
} };
note(); window.__log.length = 0;
new MutationObserver(note).observe(list, { childList: true, subtree: true });
document.addEventListener('visibilitychange',
  () => window.__log.push({ vis: document.visibilityState, at: new Date().toISOString() }));
```

Then speak from another device and compare `window.__log` against:

```bash
gcloud logging read \
  'resource.labels.container_name="actual-chat-app"
   AND timestamp>="<T0>" AND timestamp<="<T1>"
   AND textPayload:"<chatId>" AND textPayload:"PushStream: AudioRecord"' \
  --project=actual-chat-app-dev --format='value(timestamp, textPayload)' --order=asc
```

**Always check `visibilityState`** in the log before trusting any timing.

## What this is not

Not the recorder, and not the network. Same session, same chat, same phone
produced 0.76 s uploads throughout — including during the 60 s browser stalls.
