# Voxt

![unit tests](https://github.com/Actual-Chat/actual-chat/actions/workflows/build-test-deploy-dev.yml/badge.svg)

![slow tests](https://github.com/Actual-Chat/actual-chat/actions/workflows/test-slow.yml/badge.svg)

![nightly tests](https://github.com/Actual-Chat/actual-chat/actions/workflows/test-nightly.yml/badge.svg?branch=dev)

**Voxt** is a real-time communication platform built with .NET, Blazor, and [ActualLab.Fusion](https://github.com/ActualLab/Fusion).

Web site: [voxt.ai](https://voxt.ai)

## Documentation for Developers

For comprehensive documentation for developers, see the **[Voxt Documentation](./docs/index.md)**.

## Key Technologies

| Component | Technology                                              |
|:----------|---------------------------------------------------------|
| Backend   | .NET 10, C# 14                                          |
| Fusion    | [ActualLab.Fusion](https://github.com/ActualLab/Fusion) |
| UI        | Blazor (Server/WebAssembly), TypeScript                 |
| Mobile    | .NET MAUI                                               |

## Quick Start

### Prerequisites

- [Git](https://git-scm.com/downloads)
- [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/get-started)
- [Node.js 20+](https://nodejs.org/en/)

### Setup

```bash
# Restore tools and workloads
dotnet tool restore
dotnet workload install wasm-tools maui aspire

# Install npm dependencies
./npm-install.cmd

# Start infrastructure (PostgreSQL, Redis, NGINX, etc.)
./docker-start.cmd

# Build and run with watch
./run-build.cmd watch

# Or with auto git pull/reset
./run-watch.cmd --git-sync pull
```

Access the app at https://local.voxt.ai (see [Running Voxt](./docs/running-voxt.md) for host setup).

## Join Team Chats

- Our team uses [Voxt](https://voxt.ai) to communicate
- Contact [Alex Yakunin](https://voxt.ai/u/hjp639qb6bp1) to get access

## Conventions

- [Conventional commits](https://www.conventionalcommits.org/en/v1.0.0/)
- See [Coding Style](./docs/CODING_STYLE.md) for .NET coding guidelines

## Releases

We use [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning/blob/master/doc/nbgv-cli.md).

```bash
dotnet nbgv prepare-release
git push --set-upstream origin release/vX.Y
```

Release deployment requires approval from core team members.

## Related Projects

- [ActualLab.Fusion](https://github.com/ActualLab/Fusion) - Real-time state synchronization framework
- [ActualLab.Fusion.Samples](https://github.com/ActualLab/Fusion.Samples) - Sample applications
