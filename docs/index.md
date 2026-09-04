---
layout: home

hero:
  name: "Voxt"
  text: "Real-Time Communication Platform"
  tagline: Built with .NET, Blazor, and ActualLab.Fusion
  actions:
    - theme: brand
      text: Architecture
      link: /architecture/overview
    - theme: alt
      text: Development
      link: /running-voxt
    - theme: alt
      text: Testing
      link: /testing/overview
    - theme: alt
      text: Live Video
      link: /live-video/README
    - theme: alt
      text: Live Audio
      link: /live-audio/README
    - theme: alt
      text: UI
      link: /ui/
    - theme: alt
      text: Plans
      link: /plans/
    - theme: alt
      text: GitHub
      link: https://github.com/ActualChat/ActualChat

features:
  - title: Real-Time Voice & Text
    details: Seamless voice and text messaging with live audio streaming, real-time transcription, and instant message delivery.
  - title: Live Transcription
    details: Speech-to-text powered by Deepgram and OpenAI Whisper, enabling searchable voice messages and accessibility.
  - title: AI-Powered Features
    details: Integrated AI capabilities including chat summarization, smart notifications, and context-aware assistance.
  - title: Cross-Platform
    details: Single codebase serving Web (Blazor), iOS, Android, and Windows via .NET MAUI with shared UI components.
---

## Key Technologies

| Component | Technology |
|-----------|------------|
| Backend | .NET 11, C# 15 |
| Real-time sync | [ActualLab.Fusion](https://github.com/ActualLab/Fusion) |
| UI | Blazor (Server/WebAssembly), TypeScript |
| Databases | PostgreSQL, Redis |
| Messaging | NATS |
| Mobile | .NET MAUI |

## Pipelines

- [Live Video pipeline](./live-video/README.md) — end-to-end documentation of the
  current video pipeline: capture, encoding, simulcast, RPC fan-out,
  playback, quality control, buffering goals, A/V sync.
- [Live Audio pipeline](./live-audio/README.md) — end-to-end documentation of the
  current audio pipeline: microphone capture, VAD, Opus encoding,
  publish/persist/transcribe, fan-out, replay, playback.

## UI

- [UI documentation](./ui/index.md) — component conventions, the virtual list
  specification, safe areas, and splash screens.

## Cross-cutting

- [Localization (i18n)](./i18n.md) — the catalog mechanism, how to add and
  consume a key, plurals and sentence fragments, writing English that survives
  translation, product terminology, and the enumerated list of what stays
  English on purpose.

- [Compute-method invalidation map](./invalidation-map.md) — where invalidations
  originate, how they amplify, which edges are conditional, and where
  `ConsolidationDelay` cuts a cascade that is effectively a no-op.
- [Command idempotency](./architecture/command-idempotency.md) — how `ApiCommand`'s
  client-generated `Uuid` + an in-process server filter dedup retried commands, and how
  the version-gated deserializer keeps old clients working across a rollout.
- [App updates](./app-updates.md) — how the "Update Voxt" banner learns that a
  newer build is actually published in the user's store, per app kind, and what
  that costs the release process.

## Related Projects

- [ActualLab.Fusion](https://github.com/ActualLab/Fusion) - The real-time state synchronization framework powering Voxt
- [ActualLab.Fusion.Samples](https://github.com/ActualLab/Fusion.Samples) - Sample applications demonstrating Fusion patterns
