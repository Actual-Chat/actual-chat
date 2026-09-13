---
name: memories
description: |
  What this project has already learned the hard way, and where any new such fact belongs. Two
  jobs. (1) Routing: this project keeps NO per-project Claude memories — read this before writing
  anything down, and pick a durable home from the list here. (2) An index, by area, of non-obvious
  facts worth knowing before you guess: debugging rigs and measurements that lie, iOS/WebKit and
  the tethered iPhone, the Windows app, production and deployment, and general codebase traps.
  Use before setting up a browser- or server-driven debugging session, when a bug reproduces only
  on iPhone, in Safari or in the Windows app, before changing deployment configuration or reading
  store/crash data, when something behaves unexpectedly and you are about to guess why, and
  whenever you are tempted to record a fact for later.
allowed-tools:
  - Read
  - Grep
  - Glob
  - Bash
---

# Project memory

## Where a new fact goes

**This project — and every `ActualChat-*` clone or sibling (C1, C2, Marketing, …) — keeps no
per-project Claude memories.** Those `memory/` directories stay empty. A fact learned here
belongs somewhere durable: checked in, visible to every clone, and readable by Alex.

**Ask Alex where it should go**, and offer the destinations:

| Destination | For |
|---|---|
| `areas/*.md` here | A project quirk — a couple of sentences, full write-up in `references/<area>/` |
| `docs/CODING_STYLE.md` | Anything about how code in this repo should be written |
| `docs/**` | How a subsystem actually behaves (`live-video/`, `live-audio/`, `ui/`, …) |
| The owning `.claude/commands/*.md` or skill | When the fact is really "how to use this tool correctly" |
| `AGENTS-Source.md` | A rule every agent must have in context in every session (then run `ai update-md`) |
| A GitHub issue | Open follow-up work — not a memory |
| `~/.claude/CLAUDE.md` | Only if it is about Alex or his machine, not about this project |
| Nowhere | A one-off finding about a bug that is already fixed |

**Why:** Claude memories are per-project-directory and do not cross the Docker (`-proj-…`) and
Windows (`D--Projects-…`) views of the same repo, so anything written there is duplicated,
invisible from the other side, and lost to everyone else. Everything above is in git.

**Before adding an entry, check it is still true.** Several memories migrated here in September
2026 named symbols that no longer existed. An entry that describes code is worth only as much as
its last verification.

## Areas

| Area | What it covers |
|---|---|
| [Debugging](areas/debugging.md) | Servers and builds, the browser rigs, measurements that lie, calling a running server |
| [iOS / WebKit](areas/ios.md) | Driving and profiling the tethered iPhone, channels that lie, Safari, builds and signing |
| [Windows](areas/windows.md) | Diagnosing the WinUI/WebView2 app, native stack sampling, echo cancellation |
| [Infrastructure](areas/infra.md) | Deployment and Flux, reaching the clusters, the external consoles |
| [General](areas/general.md) | Performance traps and build-tool traps in the codebase itself |

Each area file is a short index; the long write-up for an entry lives in
`references/<area>/<name>.md`.

Related skills with their own depth: `/server-loop`, `/debug-ui`, `/virtual-list-debug`,
`/gcloud-dev-logs`, `/gcloud-prod-logs`, `/soniox-sweep`, and the user-level `/macmini`.
