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
      text: Plans
      link: /plans/BigTasks
    - theme: alt
      text: GitHub
      link: https://github.com/ActualChat/ActualChat

features:
  - title: Real-Time Sync
    details: Powered by ActualLab.Fusion for transparent real-time state synchronization between server and clients.
  - title: Two-Tier Services
    details: Clean separation between Frontend services (session-based auth) and Backend services (resolved identities).
  - title: Modern Stack
    details: .NET 9, C# 13, Blazor, PostgreSQL, Redis, NATS, and .NET MAUI for cross-platform mobile.
---

## Key Technologies

| Component | Technology |
|-----------|------------|
| Backend | .NET 9, C# 13 |
| Real-time sync | [ActualLab.Fusion](https://github.com/ActualLab/Fusion) |
| UI | Blazor (Server/WebAssembly), TypeScript |
| Databases | PostgreSQL, Redis |
| Messaging | NATS |
| Mobile | .NET MAUI |

## Related Projects

- [ActualLab.Fusion](https://github.com/ActualLab/Fusion) - The real-time state synchronization framework powering Voxt
- [ActualLab.Fusion.Samples](https://github.com/ActualLab/Fusion.Samples) - Sample applications demonstrating Fusion patterns
