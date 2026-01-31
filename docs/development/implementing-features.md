# Implementing End-to-End Features

This guide describes how to implement a full-stack feature that spans multiple layers of the ActualChat architecture. We use the **Chat Roulette** feature as a reference implementation, as it touches virtually every part of the system.

> **Reference Commit**: [`ebc6c30b`](https://github.com/Actual-Chat/actual-chat/commit/ebc6c30b9534474e764c5366d905e3ec0af62bc7)
> This commit shows all components required for a complete feature implementation (89 files, ~4,200 lines).

## Table of Contents

1. [Overview](#overview)
2. [Architecture Layers](#architecture-layers)
3. [Implementation Checklist](#implementation-checklist)
4. [Detailed Component Guide](#detailed-component-guide)
   - [Domain Models (Api)](#1-domain-models-api)
   - [Identifiers](#2-identifiers)
   - [Frontend Service Contracts](#3-frontend-service-contracts-apicontracts)
   - [Backend Service Contracts](#4-backend-service-contracts-domaincontracts)
   - [Backend Service Implementation](#5-backend-service-implementation)
   - [Frontend Service Implementation](#6-frontend-service-implementation)
   - [Database Layer](#7-database-layer)
   - [Events](#8-events)
   - [UI Services](#9-ui-services)
   - [UI Components](#10-ui-components)
   - [Feature Flags](#11-feature-flags)
   - [Module Registration](#12-module-registration)
   - [Static Resources](#13-static-resources)
   - [Integration with Existing Services](#14-integration-with-existing-services)
5. [Testing Strategy](#testing-strategy)
6. [Common Patterns](#common-patterns)
7. [Cleanup Considerations](#cleanup-considerations)

---

## Overview

An end-to-end feature in ActualChat typically requires changes across:

| Layer | Projects | Purpose |
|-------|----------|---------|
| **Domain Models** | `Api` | Data structures, identifiers |
| **Frontend Contracts** | `Api.Contracts` | Client-facing service interfaces |
| **Backend Contracts** | `{Domain}.Contracts` | Server-side service interfaces |
| **Backend Services** | `{Domain}.Service` | Business logic implementation |
| **Database** | `{Domain}.Service/Db` | Entity persistence |
| **Events** | `Backend/Events` | Cross-service communication |
| **UI Services** | `UI.Blazor.App/Services` | Client-side state management |
| **UI Components** | `UI.Blazor.App/Components` | Blazor UI |
| **Feature Flags** | `UI.Blazor/Services/Features` | Gradual rollout control |

---

## Architecture Layers

```
┌─────────────────────────────────────────────────────────────────┐
│                        UI Components                            │
│  (Blazor Razor Components, TypeScript/Lit Elements, CSS)        │
├─────────────────────────────────────────────────────────────────┤
│                         UI Services                             │
│  (RouletteUI, State Management, Computed Properties)            │
├─────────────────────────────────────────────────────────────────┤
│                    Frontend Service Clients                     │
│  (IRoulette, IRouletteProfiles - Fusion RPC clients)            │
├─────────────────────────────────────────────────────────────────┤
│                    Frontend Service Impl                        │
│  (Roulette.cs - validates, delegates to backend)                │
├─────────────────────────────────────────────────────────────────┤
│                       Backend Services                          │
│  (RouletteBackend.cs, RouletteProfilesBackend.cs)               │
├─────────────────────────────────────────────────────────────────┤
│                       Database Layer                            │
│  (DbChatRoulette, DbRouletteProfile, EF Core)                   │
└─────────────────────────────────────────────────────────────────┘
```

---

## Implementation Checklist

Use this checklist when implementing a new feature:

### Domain Layer
- Define domain models in `Api/{Feature}/`
- Create identifier types if needed in `Api/Identifiers/`
- Register identifier formatters in `Api/Module/ApiModuleInitializer.cs`
- Add constants in `Api/Constants.cs`
- Update related extension methods (e.g., `ChatExt.cs`)

### Contract Layer
- Define frontend interfaces in `Api.Contracts/{Feature}/`
- Define backend interfaces in `{Domain}.Contracts/`
- Register frontend clients in `Api.Contracts/Module/ApiContractsModule.cs`

### Service Layer
- Implement backend services in `{Domain}.Service/`
- Implement frontend services (if needed) in `{Domain}.Service/`
- Register services in `{Domain}.Service/Module/{Domain}ServiceModule.cs`

### Database Layer
- Create database entities in `{Domain}.Service/Db/`
- Configure entities in `{Domain}DbContext.cs`
- Add migrations in `{Domain}.Service.Migration/`
- Register entity resolvers in module

### Events
- Define events in `Backend/Events/`
- Implement event handlers in relevant services

### UI Layer
- Create UI service in `UI.Blazor.App/Services/`
- Register UI service in `BlazorUIAppModule.cs`
- Add property to `AppUIHub.cs`
- Create Blazor components in `UI.Blazor.App/Components/{Feature}/`
- Add CSS styles
- Register modals/popups in module
- Update dynamic dependencies in `ClientStartup.cs` (for AOT)

### Feature Flags
- Create feature flag in `UI.Blazor/Services/Features/`

### Resources
- Add static resources (icons, images)
- Register embedded resources in `.csproj`
- Initialize resources in `MediaDbInitializer.cs`

### Integration Points
- Update existing services that need to interact with the feature
- Add to navigation (navbar, menus)
- Add to settings if configurable
- Add to onboarding if applicable
- Update contact/chat system tags if needed

---

## Detailed Component Guide

### 1. Domain Models (Api)

Location: `src/dotnet/Api/{Feature}/`

Domain models define the data structures used across the application. They should be:
- Immutable records (use `sealed record`)
- Serializable with MemoryPack and MessagePack
- Documented with XML comments

**Example files from Chat Roulette:**
- `Profile.cs` - User profile for matching
- `Preferences.cs` - Matching preferences
- `ChatRoulette.cs` - Match result

**Pattern:**
```csharp
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Profile(
    [property: DataMember, MemoryPackOrder(0)] string Id
) : IRequirementTarget
{
    public static readonly Profile None = new("");

    [DataMember, MemoryPackOrder(1)] public Avatar Avatar { get; init; } = null!;
    [DataMember, MemoryPackOrder(2)] public Preferences Preferences { get; init; } = new();
}
```

**Key attributes:**
- `[DataContract]` - WCF/JSON serialization
- `[MemoryPackable(GenerateType.VersionTolerant)]` - Binary serialization with version tolerance
- `[MemoryPackOrder(N)]` - Explicit field ordering for compatibility

---

### 2. Identifiers

Location: `src/dotnet/Api/Identifiers/`

Custom identifier types provide type safety and parsing/formatting logic.

**Example:** `ChatRouletteId.cs`

```csharp
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(StringIdJsonConverter<ChatRouletteId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdNewtonsoftJsonConverter<ChatRouletteId>))]
[TypeConverter(typeof(StringIdTypeConverter<ChatRouletteId>))]
public readonly partial record struct ChatRouletteId : IStringId<ChatRouletteId>
{
    public static ChatRouletteId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    public ChatRouletteId(Symbol id) => Id = id;
    public ChatRouletteId(string id) : this(new Symbol(id)) { }

    // Parsing and formatting methods...
}
```

**Registration in `ApiModuleInitializer.cs`:**
```csharp
// Roulette identifiers
MemoryPackFormatterProvider.Register(
    new StringIdentifierMemoryPackFormatter<ChatRouletteId>());
```

---

### 3. Frontend Service Contracts (Api.Contracts)

Location: `src/dotnet/Api.Contracts/{Feature}/`

Frontend contracts define the API surface visible to clients. Methods marked with `[ComputeMethod]` are cached and invalidated automatically.

**Example:** `IRoulette.cs`

```csharp
public interface IRoulette : IComputeService
{
    [ComputeMethod]
    Task<ChatCandidate[]> FindChatCandidates(
        Session session,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatRouletteProfiles?> GetProfiles(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task<Chat?> GetOrCreateChat(
        Roulette_GetOrCreateChat command,
        CancellationToken cancellationToken);
}
```

**Registration in `ApiContractsModule.cs`:**
```csharp
// Chat Roulette
fusion.AddClient<IRoulette>();
fusion.AddClient<IRouletteProfiles>();
```

---

### 4. Backend Service Contracts ({Domain}.Contracts)

Location: `src/dotnet/{Domain}.Contracts/`

Backend contracts define internal service APIs not exposed to clients directly.

**Example:** `IRouletteBackend.cs`

```csharp
public interface IRouletteBackend : IComputeService
{
    [ComputeMethod]
    Task<ChatRouletteFull?> GetChatRoulette(
        ChatRouletteId chatRouletteId,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatRouletteFull> OnUpsert(
        RouletteBackend_Upsert command,
        CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record RouletteBackend_Upsert(
    [property: DataMember, MemoryPackOrder(0)] ChatRouletteId Id,
    [property: DataMember, MemoryPackOrder(1)] ChatRouletteDiff Diff
) : ICommand<ChatRouletteFull>;
```

---

### 5. Backend Service Implementation

Location: `src/dotnet/{Domain}.Service/`

Backend services implement business logic and database operations.

**Example files:**
- `RouletteBackend.cs` - Chat matching logic
- `RouletteProfilesBackend.cs` - Profile management

**Pattern:**
```csharp
public class RouletteBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IRouletteBackend
{
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<ChatRouletteFull?> GetChatRoulette(
        ChatRouletteId chatRouletteId,
        CancellationToken cancellationToken)
    {
        // Database query with caching
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbEntity = await dbContext.ChatRoulettes
            .FirstOrDefaultAsync(x => x.Id == chatRouletteId.Value, cancellationToken)
            .ConfigureAwait(false);

        return dbEntity?.ToModel();
    }

    // [CommandHandler]
    public virtual async Task<ChatRouletteFull> OnUpsert(
        RouletteBackend_Upsert command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive) {
            // Invalidation phase - invalidate computed methods
            _ = GetChatRoulette(command.Id, default);
            return default!;
        }

        // Execution phase - perform database operations
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        // ... implementation
    }
}
```

---

### 6. Frontend Service Implementation

Location: `src/dotnet/{Domain}.Service/`

Frontend services validate input and delegate to backend services.

**Example:** `Roulette.cs`

```csharp
public class Roulette(IServiceProvider services) : IRoulette
{
    private IRouletteBackend Backend { get; } = services.GetRequiredService<IRouletteBackend>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();

    // [ComputeMethod]
    public virtual async Task<ChatCandidate[]> FindChatCandidates(
        Session session,
        CancellationToken cancellationToken)
    {
        // Validate session/permissions
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeActive);

        // Delegate to backend
        return await Backend.FindCandidates(account.Id, cancellationToken).ConfigureAwait(false);
    }
}
```

---

### 7. Database Layer

Location: `src/dotnet/{Domain}.Service/Db/`

#### Entity Definition

**Example:** `DbChatRoulette.cs`

```csharp
[Table("ChatRoulettes")]
[Index(nameof(ChatId), IsUnique = true)]
public class DbChatRoulette : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }

    public string ChatId { get; set; } = "";
    public string ProfileId1 { get; set; } = "";
    public string ProfileId2 { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public ChatRouletteFull ToModel() => new(ChatRouletteId.Parse(Id)) {
        Version = Version,
        ChatId = new ChatId(ChatId),
        // ... map other properties
    };
}
```

#### DbContext Configuration

**Example:** `ChatDbContext.cs`

```csharp
protected override void OnModelCreating(ModelBuilder model)
{
    // ... existing config

    var chatRoulette = model.Entity<DbChatRoulette>();
    chatRoulette.Property(e => e.Id).UseCollation("C");
    chatRoulette.Property(e => e.ChatId).UseCollation("C");
    chatRoulette.Property(e => e.ProfileId1).UseCollation("C");
    chatRoulette.Property(e => e.ProfileId2).UseCollation("C");
}
```

> **Note:** `UseCollation("C")` applies byte-by-byte comparison for identifiers, ensuring consistent sorting and fast index lookups.

#### Module Registration

**Example:** `ChatServiceModule.cs`

```csharp
// DbChatRoulette
db.AddEntityResolver<string, DbChatRoulette>();
```

---

### 8. Events

Location: `src/dotnet/Backend/Events/`

Events enable cross-service communication and side effects.

**Example:** `ChatRouletteCompletedEvent.cs`

```csharp
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatRouletteCompletedEvent(
    [property: DataMember, MemoryPackOrder(0)] ChatRouletteId ChatRouletteId,
    [property: DataMember, MemoryPackOrder(1)] UserId CompletedBy
) : EventCommand;
```

**Publishing events:**
```csharp
context.Operation.AddEvent(new ChatRouletteCompletedEvent(chatRouletteId, userId));
```

**Handling events:**
```csharp
// [EventHandler]
public virtual async Task OnChatRouletteCompletedEvent(
    ChatRouletteCompletedEvent eventCommand,
    CancellationToken cancellationToken)
{
    // Handle the event
}
```

---

### 9. UI Services

Location: `src/dotnet/UI.Blazor.App/Services/`

UI services manage client-side state and coordinate UI updates.

**Example files:**
- `RouletteUI.cs` - Main UI service
- `RouletteUI.SyncState.cs` - State synchronization

**Pattern:**
```csharp
public class RouletteUI
{
    private readonly AppUIHub _hub;
    private readonly IMutableState<RouletteState> _state;

    public IState<RouletteState> State => _state;

    public RouletteUI(AppUIHub hub)
    {
        _hub = hub;
        _state = hub.StateFactory.NewMutable(RouletteState.None);
    }

    public async Task StartChatRoulette()
    {
        // Coordinate with backend, update state
        var candidates = await _hub.Roulette.FindChatCandidates(_hub.Session, default);
        _state.Value = new RouletteState(candidates);
    }
}
```

**Registration in `BlazorUIAppModule.cs`:**
```csharp
services.AddScoped(c => new RouletteUI(c.AppUIHub()));
```

**Add to `AppUIHub.cs`:**
```csharp
public RouletteUI RouletteUI => field ??= Services.GetRequiredService<RouletteUI>();
```

---

### 10. UI Components

Location: `src/dotnet/UI.Blazor.App/Components/{Feature}/`

#### Main Page Component

**Example:** `ChatRoulettePage.razor`

```razor
@namespace ActualChat.UI.Blazor.App.Components
@inherits ComputedStateComponent<AppUIHub, ChatRoulettePage.Model>
@{
    var m = State.Value;
}

<div class="chat-roulette-page">
    <ChatRouletteHeader />
    <ChatRouletteCenter Candidates="@m.Candidates" />
    <ChatRouletteFooter />
</div>

@code {
    private RouletteUI RouletteUI => Hub.RouletteUI;

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var candidates = await RouletteUI.Candidates.Use(cancellationToken);
        return new Model(candidates);
    }

    public record Model(ChatCandidate[] Candidates);
}
```

#### Modal Registration

**In `BlazorUIAppModule.cs`:**
```csharp
services.AddTypeMap<IModalView>(map => map
    // ... existing modals
    .Add<ChatRouletteProfileModal.Model, ChatRouletteProfileModal>()
    .Add<ChatRouletteProfileEditorModal.Model, ChatRouletteProfileEditorModal>()
);
```

#### CSS Styles

**Example:** `chat-roulette.css`

Organize styles in a dedicated CSS file within the component folder.

#### TypeScript/Lit Elements

**Example:** `chat-roulette-svg.lit.ts`

For complex interactive elements, use Lit web components.

---

### 11. Feature Flags

Location: `src/dotnet/UI.Blazor/Services/Features/`

Feature flags enable gradual rollout and A/B testing.

**Example:** `Features_EnableChatRouletteUI.cs`

```csharp
public sealed class Features_EnableChatRouletteUI : FeatureBase
{
    public static Features_EnableChatRouletteUI Default { get; } = new();

    public override bool IsEnabled => false; // Default state

    public override async ValueTask<bool> ComputeIsEnabled(IServiceProvider services, CancellationToken cancellationToken)
    {
        // Check server-side feature flag, user attributes, etc.
        var serverFeatures = services.GetRequiredService<IServerFeatures>();
        return await serverFeatures.IsEnabled("ChatRoulette", cancellationToken);
    }
}
```

**Usage:**
```csharp
var enableChatRouletteUI = await Features.Get<Features_EnableChatRouletteUI>(cancellationToken);
if (enableChatRouletteUI) {
    // Show feature
}
```

---

### 12. Module Registration

Multiple module files need updates:

#### ChatServiceModule.cs
```csharp
// Service registration
fusion.AddService<IRouletteBackend, RouletteBackend>();

// Entity resolver
db.AddEntityResolver<string, DbChatRoulette>();
```

#### UsersServiceModule.cs
```csharp
fusion.AddService<IRouletteProfilesBackend, RouletteProfilesBackend>();
fusion.AddService<IRouletteProfiles, RouletteProfiles>();

db.AddEntityResolver<string, DbRouletteProfilePrefs>();
db.AddEntityResolver<string, DbRouletteUserSettings>();
```

#### ApiContractsModule.cs
```csharp
// Client registration for frontend services
fusion.AddClient<IRoulette>();
fusion.AddClient<IRouletteProfiles>();
```

#### BlazorUIAppModule.cs
```csharp
// UI service registration
services.AddScoped(c => new RouletteUI(c.AppUIHub()));

// Modal registration
.Add<ChatRouletteProfileModal.Model, ChatRouletteProfileModal>()
```

---

### 13. Static Resources

#### SVG/Image Assets

Location: `src/dotnet/Media.Service/Resources/`

**Example:** `chatroulette.svg`

#### Resource Registration

**In `Media.Service.csproj`:**
```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\chatroulette.svg" />
</ItemGroup>
```

**In `Resource.cs`:**
```csharp
public static readonly Resource ChatRoulette = new("chatroulette.svg");
```

**In `MediaDbInitializer.cs`:**
```csharp
// Add Chat Roulette image
await new MediaUploader(GetType())
    .Upload(c => c.AddMedia(ChatRoulette.MediaId.Value, Resource.ChatRoulette), cancellationToken)
    .ConfigureAwait(false);
```

---

### 14. Integration with Existing Services

A feature rarely exists in isolation. Common integration points:

#### Constants

**In `Api/Constants.cs`:**
```csharp
public static class Place {
    public static readonly PlaceId ChatRouletteId = new("chat-roulette");
}

public static class Contact {
    public static class SystemTags {
        public static readonly Symbol ChatRoulette = "chat-roulette";
    }
}

public static class Chat {
    public static class SystemTags {
        public static readonly Symbol ChatRoulette = "chat-roulette";
    }
}
```

#### Extension Methods

**In `ChatExt.cs`:**
```csharp
public static bool IsChatRoulette([NotNullWhen(true)] this Chat? chat)
    => chat is not null && chat.SystemTag == Constants.Chat.SystemTags.ChatRoulette;
```

#### Contacts Integration

Update contact handling for the new feature type:
- `ContactsBackend.cs` - Contact creation/filtering
- `DbContact.cs` - System tag handling

#### Chat Integration

Update chat services for the new feature:
- `Chats.cs` - Chat title/avatar logic
- `ChatsBackend.cs` - Chat lifecycle
- `Authors.cs` - Author restrictions

#### Navigation Integration

- `NavbarButtons.razor` - Navigation bar
- `CreateMenu.razor` - Create menu
- `LeftPanelContentHeader.razor` - Left panel

#### Settings Integration

- `SettingsModal.razor` - Settings modal
- `SettingsTabId.cs` - Tab identifiers

#### Onboarding Integration

- `OnboardingModal.razor` - Onboarding flow
- `UserOnboardingSettings.cs` - Completion tracking

---

## Testing Strategy

### Unit Tests

Location: `tests/{Domain}.UnitTests/`

Test individual components in isolation:
- Model validation
- Business logic
- Identifier parsing

### Integration Tests

Location: `tests/{Domain}.IntegrationTests/`

Test full request flows using `AppHostFixture`:
- Service interactions
- Database operations
- Event handling

### UI Tests

Location: `tests/UI.Blazor.PlaywrightTests/`

Browser automation for end-to-end UI testing.

---

## Common Patterns

### Command/Handler Pattern

```csharp
// Command definition
[DataContract, MemoryPackable]
public sealed partial record Roulette_GetOrCreateChat(
    [property: DataMember] Session Session,
    [property: DataMember] string ProfileId
) : ISessionCommand<Chat?>;

// Handler implementation
[CommandHandler]
public virtual async Task<Chat?> GetOrCreateChat(
    Roulette_GetOrCreateChat command,
    CancellationToken cancellationToken)
{
    // Validate, execute, return
}
```

### Computed State in Blazor

```razor
@inherits ComputedStateComponent<AppUIHub, MyComponent.Model>

@code {
    protected override ComputedState<Model>.Options GetStateOptions()
        => new() {
            InitialValue = Model.None,
            Category = GetStateCategory(GetType()),
        };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        // Fetch and compute state
    }
}
```

### Invalidation Pattern

```csharp
if (Invalidation.IsActive) {
    // Invalidation phase - notify what needs to be recomputed
    _ = GetSomething(id, default);
    return default!;
}

// Execution phase - perform actual work
```

---

## Cleanup Considerations

When removing a feature, reverse the implementation order:

1. Remove UI components and registrations
2. Remove UI services
3. Remove feature flags
4. Remove frontend service clients and contracts
5. Remove frontend service implementations
6. Remove backend service implementations and contracts
7. Remove database entities (consider data migration)
8. Remove domain models and identifiers
9. Remove static resources
10. Update integration points (remove checks, system tags, etc.)
11. Remove constants

> **Important:** Database migrations should be additive. When removing entities, consider:
> - Keeping tables for historical data
> - Creating a migration that marks tables as deprecated
> - Planning data migration before schema changes

---

## See Also

- [Project Structure](../architecture/project-structure.md) - Overall codebase organization
- [Service Design](../architecture/service-design.md) - Two-tier service architecture
- [Architecture Overview](../architecture/overview.md) - System architecture
