---
title: Plans
description: Index of all project plans and task lists.
---

# Plans

Task lists and design/implementation plans, roughly grouped by area.

## Task lists

- [Big tasks](./BigTasks.md)
- [Small tasks](./SmallTasks.md)

## Architecture and infrastructure

- [Distributed services](./distributed-services.md) — migrating off operations-framework invalidation
- [Database sharding](./db-sharding.md) — sharding for backend services
- [On-premises instances](./on-prem-instances.md)
- [MessagePack migration](./msgpack.md)
- [Live streams](./live-streams.md) — multiplexed streaming service

## Search

- [MLSearch: OpenSearch → PostgreSQL](./mlsearch-postgres-fts.md) — tsvector-based search with app-level multilingual analysis
- [Search](./Search.md) — original (2024) search architecture notes

## Chat and UI

- [Chat entry redesign](./chat-entry-redesign.md) — eliminating audio entries
- [Universal mentions](./uni-mentions.md) — implementation record
- [Auto-fetch avatars](./auto-fetch-avatars.md) — user profile picture flow
- [Speech render performance](./speech-render-perf.md) — rendering improvements during speech

## Audio and video

- [Audio diagnostics](./audio-diagnostics.md) — diagnostics UI (#4029)
- [NAudio replacement](./naudio-replacement.md) — Windows audio on NativeAOT
- [Video quality control v2](./video-quality-control-v2.md)
- [Video simulcast](./video-simulcast.md) — long-term
- [Video throughput probing](./video-throughput-probing.md) — medium-term
- [Video rotation](./video-rotation.md)

## Notifications

- [Notifications redesign](./notif-api.md) — `feat/notif-api`
- [Notifications redesign review](./notif-api-review.md) — PR #3892 review

## Accounts and security

- [E2E encryption](./e2ee.md)
- [User and account merge](./user-account-merge.md)

## Platform: macOS / Mac Catalyst

- [Voice processing](./maccatalyst-voice-processing.md) — restore AVAudioEngine AEC / NS / AGC
- [Notification permission](./macos-notification-permission.md) — wiring the "Configure" button

## Build, testing, CI

- [E2E tests in CI](./e2e-ci.md) — running E2E and TS unit tests from CI and locally
- [E2E nightly fixes](./e2e-nightly-fixes.md)
- [TypeScript `moduleResolution: bundler`](./ts-module-resolution-bundler.md)
