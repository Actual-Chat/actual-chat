---
name: shared-resources
description: |
  Use when more than one agent may need the same single-instance resource at
  the same time — most of all the local server loop, which holds the one URL
  (https://local.voxt.ai, port 7080) the app can run on, and the Chrome
  instances that go with it. Also when you are about to dispatch several agents
  that will test the UI, when you find another agent already using the loop, or
  when the user says "coordinate", "shared resource", "queue for the server".
---

# Shared resources — one coordinator agent, a queue, a lease

A shared resource is something only one agent can use at a time and that has no
arbitration of its own. Today that is the **server loop** (it owns one URL, so
one running copy of the app, built from one worktree) and, with it, the Chrome
instances used for UI checks. More may come; the protocol is the same.

Agents do not share such a resource by sharing a setup or by trial and error.
**One agent — the coordinator — owns the resource. Everyone else asks it for the
resource, waits in its queue, and tells it when they are done.**

## Do you need a coordinator at all?

| Situation | Do |
|---|---|
| You are the only agent, or the user said "the server loop is yours" / "use the loop for this" | It is yours. Use it. Start it yourself if it is not running (see `/server-loop`). No coordinator. |
| You were not told it is yours, and nothing else is running | You may use it and start it, but treat any other agent that appears as a reason to hand over to a coordinator. |
| Two or more agents may need the resource within the same window | A coordinator is required. |

**Who starts the coordinator.** Either the **parent agent** that is about to
dispatch several agents that will need the resource, or the **first agent that
notices** a second user (another agent is using the loop, `ListAgents` shows
agents that will test the UI, a restart you did not make). Before starting one,
run `ListAgents` and look for an existing coordinator for that resource; there
must be exactly one per resource. If you start it, tell every agent that is
already running (`SendMessage`) and put its id/name in the prompt of every agent
you dispatch afterwards.

## The coordinator

A dedicated subagent that does nothing but this. Brief it with: the resource(s)
it coordinates, this file, and (for the server loop) `/server-loop` and
`/debug-ui`. It works from a worktree of its own; it never edits product code.

It keeps, per resource: the **current owner**, the **queue**, and when each was
last confirmed alive. It also writes the same state to a small file under
`tmp/` of the main checkout (`tmp/shared-resources/<resource>.md`) so a
replacement coordinator can recover it.

### Messages

All through `SendMessage`, to the coordinator by name or id.

| From a user agent | Meaning |
|---|---|
| `REQUEST <resource> <worktree path> <what for, how long>` | Put me in the queue. The worktree is where my code is; the server loop has to run from there. |
| `RELEASE <resource>` | I am finished. Stop treating it as mine. |
| `EXTEND <resource> <what for>` | I still need it (answers a poll). |

| From the coordinator | Meaning |
|---|---|
| `GRANTED <resource> <how to reach it>` | It is yours now, ready to use (for the loop: URL, worktree it runs from, which Chrome and which test user you may use). |
| `QUEUED <resource> position N` | Wait. Do not touch the resource. |
| `STILL USING?` | Poll. Answer with `EXTEND` or `RELEASE`. |
| `REVOKED <resource> <why>` | You no longer own it. Stop at once. |

An agent that has asked and not yet been granted does not use the resource, does
not "just check one thing", and does not restart anything. Use the waiting time
for work that does not need it (reading code, writing the test, preparing the
fix).

### Queue discipline

- First come, first served. A request from the same agent while it is the owner
  is an extension, not a new place in the queue.
- The owner is assumed to be using it as long as it is **running and working**.
  Do not take it away because someone is waiting; the point of the queue is that
  waiting is cheap.
- **Poll the owner about every 3 minutes while the queue is not empty.** Use
  `ListAgents` to see whether the owner is still running, and send `STILL
  USING?`. An owner that has stopped, finished or crashed, or that answers
  `RELEASE`, loses the resource. An owner that does not answer but is still
  running gets one more poll interval, then `REVOKED`.
- When the queue is empty there is nothing to poll; check again when the next
  `REQUEST` arrives.
- Keep it fair: one agent does not hold the resource across several unrelated
  tasks. If a finished owner sends a new `REQUEST`, it goes to the back.

### Handing the resource over

The coordinator does the switch, not the new owner, so the old and new state can
never overlap:

1. Make sure the old owner has stopped (`RELEASE` received, or revoked).
2. Prepare the resource for the new owner (for the server loop: see below).
3. Verify it is ready (for the loop: a 200 from `http://localhost:7080/healthz/live`).
4. Send `GRANTED`.
5. Update the state file.

## The server loop as a shared resource

The loop runs **in one directory**: the worktree it was started from. It builds
and serves that worktree's code, so the owner's code has to be the code the loop
runs. Handing the loop over therefore means **stopping it and starting a new one
from the new owner's worktree**:

1. Stop the running loop: end its window's process tree (the loop's own `k`
   needs a keypress, and a restart via `/health/stop` only restarts it in the
   same directory). Check that port 7080 and the server process are gone
   (`/server-port-check`).
2. Start `server-loop.cmd` from the new owner's worktree, **in its own visible
   window** on the host, as described in `/server-loop` → **How to start it**.
3. Wait for `Step 3/3 (server-run)` and a 200 from the app URL.
4. `GRANTED`.

If the new owner uses the same worktree as the previous one, nothing needs to be
restarted; verify and grant.

While an agent owns the loop it may use the restart/rebundle rights described in
`/server-loop` → **Coordinating subagents** (rebundle, `/health/stop`,
hard restart), because nobody else is using it. The Chrome instance and test
user it may use are part of the grant; one agent per Chrome.

The coordinator never grants the loop to an agent that runs in Docker as if that
agent could start it itself: agents in Docker use the loop that runs on the host
and do not start their own. Starting and stopping loops on the host is the
coordinator's job (or the user's).

## Rules for agents that use a resource

1. Do not use or restart a shared resource that has a coordinator without a
   `GRANTED`.
2. When you are done, send `RELEASE` — even if you stopped early, the task was
   cancelled or you hit an error. An owner that disappears without a `RELEASE`
   keeps the others waiting for up to one poll interval.
3. Answer `STILL USING?` promptly.
4. Obey `REVOKED` immediately; save your evidence (logs, screenshots) first if
   it takes seconds, not minutes.
5. If you need a different resource than the one you hold (for example the
   second Chrome), ask for it; do not take it.

## Common mistakes

| Mistake | Fix |
|---|---|
| Two agents each start their own loop, or one restarts the loop under the other | One coordinator, one owner at a time |
| Starting a coordinator when you were told the loop is yours | You do not need one until a second user appears |
| Starting a second coordinator | `ListAgents` first; one per resource |
| Not releasing at the end | `RELEASE` is part of the task, like pushing |
| Taking the loop away from a working owner because someone is waiting | Only an owner that stopped, crashed or released loses it |
| Starting the loop as a hidden background process or inside Docker | Visible window on the host only; see `/server-loop` |
