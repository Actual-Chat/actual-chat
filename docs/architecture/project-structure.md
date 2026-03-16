# Project Structure

This document describes the organization of the Voxt (ActualChat) codebase.

## Solution Files

| File | Purpose |
|------|---------|
| `ActualChat.sln` | Main solution with all projects |
| `ActualChat.CI.slnf` | CI solution filter (excludes MAUI projects) |

**Always use `ActualChat.CI.slnf` for building** unless you have MAUI workloads installed.

## Directory Layout

```
ActualChat/
├── src/
│   ├── dotnet/           # All .NET projects
│   │   ├── Core/         # Shared utilities
│   │   ├── App.*/        # Application entry points
│   │   ├── {Domain}.*/   # Domain services
│   │   └── UI.*/         # UI projects
│   └── nodejs/           # TypeScript utilities
├── tests/                # Test projects
├── docs/                 # Documentation
├── build/                # Build scripts
└── artifacts/            # Build output
```

## Core Infrastructure Projects

### Foundation Layer

| Project | Namespace | Purpose |
|---------|-----------|---------|
| `Core` | `ActualChat` | Shared utilities, extensions, constants |
| `Core.Server` | `ActualChat.Server` | Server infrastructure (auth, flows, blobs) |
| `Core.Audio` | `ActualChat.Audio` | Audio processing and encoding |
| `Db` | `ActualChat.Db` | Database models and EF Core setup |
| `Redis` | `ActualChat.Redis` | Redis client wrapper |
| `Backend` | `ActualChat.Backend` | Event definitions, sharding |

### Application Entry Points

| Project | Purpose | Platform |
|---------|---------|----------|
| `App.Server` | ASP.NET Core server (API + Blazor) | Server |
| `App.Wasm` | WebAssembly client | Browser |
| `App.Maui` | Cross-platform mobile app | iOS, Android, Windows |
| `App.ConsoleClient` | Console test client | CLI |
| `App.AspireHost` | .NET Aspire orchestration | Development |

## Domain Services

Each domain follows the two-tier architecture with separate frontend and backend services.

### Service Project Structure

For each domain `{Domain}`, you'll find:

```
{Domain}.Contracts/    # Interface definitions and commands
{Domain}.Service/      # Service implementations
{Domain}.Service.Migration/  # EF Core migrations (if applicable)
```

### Domain Overview

| Domain | Frontend API | Backend API | Description |
|--------|--------------|-------------|-------------|
| **Chat** | `IChats` | `IChatsBackend` | Conversations and messages |
| **Users** | `IAccounts` | `IAccountsBackend` | User accounts and sessions |
| **Contacts** | `IContacts` | `IContactsBackend` | Contact lists |
| **Media** | `IMedia` | `IMediaBackend` | Media uploads and processing |
| **Streaming** | - | `IStreamingBackend` | Real-time audio/video |
| **Transcription** | - | `ITranscriptionBackend` | Speech-to-text |
| **Notification** | - | `INotificationBackend` | Push notifications |
| **Invite** | - | `IInviteBackend` | Invitation links |
| **MLSearch** | - | `IMLSearchBackend` | AI-powered search |
| **Chat.ML** | - | `IChatCompletionService` | AI chat features |

### Key Files in Service Projects

```
{Domain}.Service/
├── {Domain}Backend.cs        # Backend service implementation
├── {Domain}s.cs              # Frontend service (if exists)
├── Db/
│   └── {Domain}DbContext.cs  # EF Core DbContext
├── Module/
│   └── {Domain}ServiceModule.cs  # DI registration
└── Internal/                 # Internal helpers
```

## Contract Projects

Located in `src/dotnet/{Domain}.Contracts/`:

### API Contracts

| Project | Location | Purpose |
|---------|----------|---------|
| `Api.Contracts` | `src/dotnet/Api.Contracts/` | Frontend service interfaces |
| `{Domain}.Contracts` | `src/dotnet/{Domain}.Contracts/` | Backend service interfaces |

### Contract File Organization

```
{Domain}.Contracts/
├── I{Domain}Backend.cs       # Backend service interface
├── {Entity}.cs               # Domain models
├── {Entity}Diff.cs           # Change models for commands
├── Events/
│   └── {Entity}ChangedEvent.cs  # Event definitions
└── Commands/                 # Command definitions (if separate)
```

## UI Projects

### Blazor Stack

| Project | Purpose |
|---------|---------|
| `UI` | Shared UI utilities |
| `UI.App` | App shell components |
| `UI.Blazor` | Core Blazor infrastructure |
| `UI.Blazor.App` | Application components |

### UI Organization

```
UI.Blazor.App/
├── Components/
│   ├── Chat/
│   │   ├── ChatView.razor
│   │   └── ChatView.razor.ts    # TypeScript interop
│   ├── Contacts/
│   └── ...
├── Services/
│   └── UI-level services
└── wwwroot/
    └── Static assets
```

## TypeScript Projects

Source files are in `src/nodejs/`, while config files (`package.json`, `tsconfig.json`, `eslint.config.mjs`, etc.) are in the project root:

```
src/nodejs/
├── src/
│   ├── connectivity.ts      # Network monitoring
│   ├── device-info.ts       # Device detection
│   ├── dom-helpers.ts       # DOM utilities
│   ├── event-handling.ts    # Event system
│   ├── gestures.ts          # Touch handling
│   └── ...
├── styles/
│   └── Tailwind styles
├── index.ts                 # Main entry point
└── types/                   # TypeScript type definitions
```

## Test Projects

Located in `tests/`:

### Test Organization

```
tests/
├── Testing/                  # Base testing framework
├── Testing.Host/             # Shared AppHostFixture
├── {Domain}.UnitTests/       # Unit tests
├── {Domain}.IntegrationTests/ # Integration tests
└── UI.Blazor.PlaywrightTests/ # Browser automation
```

### Key Test Infrastructure

| Project | Purpose |
|---------|---------|
| `Testing` | Base classes, utilities, extensions |
| `Testing.Host` | `AppHostFixture` for full-stack integration tests |

### AppHostFixture

The shared test host provides:
- Full application stack with NATS queues
- Test database (PostgreSQL)
- Unique user creation for test isolation
- Queue processing utilities

## Build Infrastructure

### Key Files

| File | Purpose |
|------|---------|
| `Directory.Build.props` | Shared MSBuild properties |
| `Directory.Packages.props` | Centralized NuGet versions |
| `global.json` | .NET SDK version |
| `docker-compose.yml` | Infrastructure services |

### Build Scripts

| Script | Purpose |
|--------|---------|
| `run-build.cmd` | Main build orchestration |
| `run-watch.cmd` | Build watch with optional git sync (`--git-sync pull\|reset`) |
| `npm-install.cmd` | Install Node dependencies |
| `npm-build.cmd` | Build TypeScript |
| `docker-start.cmd` | Start infrastructure |

## Namespace Conventions

### Root Namespace

All code lives under `ActualChat` namespace.

### Domain Namespaces

```csharp
ActualChat.Chat       // Chat domain
ActualChat.Users      // Users domain
ActualChat.Contacts   // Contacts domain
ActualChat.Media      // Media domain
// etc.
```

### Infrastructure Namespaces

```csharp
ActualChat.Server     // Server infrastructure
ActualChat.Db         // Database layer
ActualChat.Audio      // Audio processing
ActualChat.Backend    // Events, sharding
ActualChat.Redis      // Redis integration
```

## Finding Code

### By Feature

1. **Frontend API** → `src/dotnet/Api.Contracts/`
2. **Backend service** → `src/dotnet/{Domain}.Contracts/` for interface, `src/dotnet/{Domain}.Service/` for implementation
3. **UI component** → `src/dotnet/UI.Blazor.App/Components/{Domain}/`
4. **Database entity** → `src/dotnet/{Domain}.Service/Db/`

### By Layer

| Layer | Location |
|-------|----------|
| API Controllers | `App.Server/Controllers/` |
| Frontend Services | `{Domain}.Service/{Domain}s.cs` |
| Backend Services | `{Domain}.Service/{Domain}Backend.cs` |
| Database | `{Domain}.Service/Db/` |
| Events | `{Domain}.Contracts/Events/` or `Backend/Events/` |

### By File Type

| Looking for | Pattern |
|-------------|---------|
| Service interface | `I{Domain}Backend.cs` or `I{Domain}s.cs` |
| Command definition | `{ServiceName}_{Action}` in contracts |
| Event definition | `{Entity}ChangedEvent.cs` |
| Blazor component | `{Name}.razor` |
| TypeScript interop | `{Name}.razor.ts` |
| Database migration | `{Domain}.Service.Migration/Migrations/` |

## Key Entities by Domain

### Chat Domain

| Entity | Description |
|--------|-------------|
| `Chat` | Conversation (group, peer, or place) |
| `ChatEntry` | Message or event in a chat |
| `ChatTile` | Paginated message block |
| `Author` | User's presence in a chat |

### Users Domain

| Entity | Description |
|--------|-------------|
| `Account` | User account with profile |
| `Session` | Client session |
| `Avatar` | User avatar |

### Contacts Domain

| Entity | Description |
|--------|-------------|
| `Contact` | Contact list entry |
| `ExternalContact` | Phone/external contact |

### Media Domain

| Entity | Description |
|--------|-------------|
| `Media` | Uploaded media file |
| `MediaLink` | Reference to media |

## Database Structure

Each service domain has its own DbContext:

```
ChatDbContext          # Chat, ChatEntry, Author
UsersDbContext         # Account, Session, Avatar
ContactsDbContext      # Contact, ExternalContact
MediaDbContext         # Media
// etc.
```

All DbContexts share the same PostgreSQL database but use different table prefixes.

## Adding New Features

### New API Endpoint

1. Add method to frontend interface in `Api.Contracts`
2. Implement in service class in `{Domain}.Service`
3. If needed, add backend interface method in `{Domain}.Contracts`

### New Domain Service

1. Create `{Domain}.Contracts` project with interface
2. Create `{Domain}.Service` project with implementation
3. Create `{Domain}.Service.Migration` if database needed
4. Register in appropriate module class
5. Add tests in `tests/{Domain}.*Tests`

### New UI Component

1. Create `.razor` file in `UI.Blazor.App/Components/{Domain}/`
2. Add `.razor.ts` if JavaScript interop needed
3. Wire up to services via DI
