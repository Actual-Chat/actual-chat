# Passkeys PR 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Existing users can add a passkey to their account (web, Windows, Android, iOS/Mac) and sign in with it instead of an OTP code; a post-sign-in nudge and an Account badge get them there.

**Architecture:** A passkey is a `UserIdentity(AuthSchema.Passkey, credentialId)` in the existing `AccountIdentities` table plus a `Passkeys` row holding the key material, owned by a new `IPasskeysBackend`. The API service `IPasskeyAuth` does the WebAuthn verification with Fido2NetLib, keeps challenges in Redis, and signs in through the existing `AccountsBackend_SignIn`. Clients share one seam, `IPasskeyClient` (`Create` / `Get` over standard WebAuthn JSON); the web implementation is JS interop, Android uses Credential Manager, Apple uses `ASAuthorizationController`.

**Tech Stack:** .NET 11 / Fusion, Fido2NetLib 4.0.0, EF Core (Postgres), Redis, Blazor, TypeScript (vitest), AndroidX Credentials, AuthenticationServices (Apple).

**Spec:** `docs/superpowers/specs/2026-09-11-passkeys-design.md` — this plan covers **PR 1** (spec §1 minus SignUp, §2, §3, §4, §7). PR 2 (passkey-first signup, recovery risk) and PR 3 (device link) get their own plans.

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing any C#/TS. No `Async` suffix; no `///` on members; comments only where the code can't say it; blank line after every control-flow statement; 120-column lines.
- Every user-visible string goes through `L.<Key>`; a new key is added to `Strings.en.json`, **every hand-written `Strings.*.json` (22 languages)** with a translation, and `LocalizedStringsLocalizerExt.cs`, then `scripts/derive-bcms.cmd` and `scripts/derive-max.cmd` are run. `AppLocalizationTest` enforces this.
- All serializable types: `[DataContract, MessagePackObject]` + `[DataMember, Key(N)]` on every member. Never MemoryPack on new types. `ApiCommand` subclasses start their keys at `Key(2)`.
- Backend commands carry `IHasShardKey<UserId>` with the four-attribute ignore on `ShardKey`.
- RP id / origins come from `UsersSettings`; nothing about the host is hardcoded on the server or in native clients.
- Feature flag: `IPasskeyAuth.IsEnabled` = `UsersSettings.IsPasskeyAuthEnabled ?? !HostInfo.IsProductionInstance`.
- TypeScript changes: run `npm run build:Verify` before committing.
- Commit after every task; never push (ask first).
- Temporary files go to `tmp/`, never the repo root.

**Deviation from the spec, decided while planning:** the spec put passkeys on an Account-row → sub-page. The settings modal has no sub-page mechanism (API keys are a tab; the "pages" are DiveIn modals), so passkeys become a **tab** (`SettingsTabId.Passkeys`, right after Account) and the Account row jumps to that tab. Playwright E2E is dropped from PR 1: every test in `UI.Blazor.PlaywrightTests` is skipped ("AppHost doesn't serve web pages"), so a new one would be dead on arrival; the web path is verified manually with Chrome DevTools → WebAuthn → virtual authenticator.

**Baseline before Task 1:** `dotnet test tests/Users.IntegrationTests --filter "FullyQualifiedName~PendingRegistrationTest|FullyQualifiedName~TotpCodesTest"` must be green (needs Postgres/Redis from `docker-compose.yml` running on the host).

---

## File map

| Path | Responsibility |
|---|---|
| `src/dotnet/Core/AuthSchema.cs` | `Passkey` schema constant + display name |
| `src/dotnet/Api/Users/UserIdentityExt.cs` | `NewPasskeyIdentity(credentialId)` |
| `src/dotnet/Api/Users/Passkey.cs` | `Passkey` API record (what the UI sees) |
| `src/dotnet/Api.Contracts/Users/IPasskeyAuth.cs` | API contract + commands + `PasskeyPurpose` |
| `src/dotnet/Users.Contracts/IPasskeysBackend.cs` | backend contract, `PasskeyCredential`, `PasskeysBackend_Change` |
| `src/dotnet/Users.Service/Db/DbPasskey.cs` | EF entity |
| `src/dotnet/Users.Service/Db/UsersDbContext.cs` | `Passkeys` DbSet |
| `src/dotnet/Users.Service.Migration/Migrations/*_Add_Passkeys.cs` | migration |
| `src/dotnet/Users.Service/Module/UsersSettings.cs` | passkey settings |
| `src/dotnet/Users.Service/Passkey/PasskeysBackend.cs` | storage + identity row + invalidation |
| `src/dotnet/Users.Service/Passkey/PasskeyAuth.cs` | WebAuthn ceremonies, challenges, sign-in |
| `src/dotnet/Users.Service/Passkey/PasskeyNames.cs` | AAGUID → default name |
| `src/dotnet/Users.Service/Module/UsersServiceModule.cs` | registrations, `IFido2` |
| `src/dotnet/Api.Contracts/Module/ApiContractsModule.cs` | `fusion.AddClient<IPasskeyAuth>()` |
| `tests/Testing/Passkeys/SoftwareAuthenticator.cs` | test authenticator (P-256) |
| `tests/Users.IntegrationTests/PasskeysBackendTest.cs`, `PasskeyAuthTest.cs` | server tests |
| `src/nodejs/src/webauthn-json.ts` + `tests/ts/unit/webauthn-json.test.ts` | WebAuthn JSON ↔ buffers (shared) |
| `src/dotnet/UI.Blazor/Services/PasskeyUI/{IPasskeyClient,PasskeyCancelledException,WebPasskeyClient,PasskeyUI}.cs`, `passkeys.ts` | client seam, web impl, orchestrator |
| `src/dotnet/UI.Blazor/UIHub.cs`, `Module/BlazorUICoreModule.cs`, `exports.ts` | wiring |
| `src/dotnet/UI.Blazor/Services/AccountUI/AccountUI.cs` | `AuthSchema.Passkey` branch |
| `src/dotnet/UI.Blazor/Components/SignIn/Modal/ProviderSelectStep.razor` | "Sign in with a passkey" button |
| `src/dotnet/UI.Blazor.App/Components/Settings/{PasskeySettings.razor,passkey-settings.css,SettingsTabId.cs,SettingsModal.razor,YourAccount.razor}` | settings tab, row, badge |
| `src/dotnet/UI.Blazor/Components/SettingsPanel/SettingsPanel.razor` | `SelectTab` |
| `src/dotnet/Api/LocalOnboardingSettings.cs`, `src/dotnet/UI.Blazor.App/Components/Onboarding/{PasskeyStep.razor,OnboardingModal.razor}`, `Services/OnboardingUI/OnboardingUI.cs` | nudge |
| `src/dotnet/App.Maui/Platforms/Android/AndroidPasskeyClient.cs`, `MauiProgram.Android.cs`, `App.Maui.csproj`, `Directory.Packages.props`, `src/dotnet/App.Wasm/wwwroot/.well-known/assetlinks.json` | Android |
| `src/dotnet/App.Maui/MaciOS/Services/{ApplePasskeyClient,ApplePasskeyJson}.cs`, `MauiProgram.{iOS,MacCatalyst,MacOS}.cs`, `Platforms/{iOS,MacCatalyst,MacOS}/Entitlements.*.plist`, `src/dotnet/App.Wasm/wwwroot/.well-known/apple-app-site-association.json` | Apple |
| `src/dotnet/Localization/Resources/*` | strings |

---

### Task 1: Schema, settings, package, DB entity + migration

**Files:**
- Modify: `src/dotnet/Core/AuthSchema.cs`
- Modify: `src/dotnet/Api/Users/UserIdentityExt.cs`
- Modify: `Directory.Packages.props`
- Modify: `src/dotnet/Users.Service/Users.Service.csproj`
- Modify: `src/dotnet/Users.Service/Module/UsersSettings.cs`
- Create: `src/dotnet/Users.Service/Db/DbPasskey.cs`
- Modify: `src/dotnet/Users.Service/Db/UsersDbContext.cs`
- Create (generated): `src/dotnet/Users.Service.Migration/Migrations/<timestamp>_Add_Passkeys.cs` + `.Designer.cs`, snapshot update

**Interfaces:**
- Produces: `AuthSchema.Passkey = "passkey"`; `UserIdentityExt.NewPasskeyIdentity(string credentialId) : UserIdentity`; `UsersSettings.IsPasskeyAuthEnabled : bool?`, `PasskeyRpId : string`, `PasskeyOrigins : string`, `PasskeyChallengeLifetime : TimeSpan`; `DbPasskey` entity; `UsersDbContext.Passkeys`.

- [ ] **Step 1: Add the schema constant**

In `src/dotnet/Core/AuthSchema.cs`, after `HashedEmail`:

```csharp
    public const string Passkey = "passkey";
```

and in `DisplayNames`:

```csharp
            [Passkey] = "Passkey",
```

- [ ] **Step 2: Add the identity helper**

In `src/dotnet/Api/Users/UserIdentityExt.cs`, next to `NewEmailIdentity`:

```csharp
    public static UserIdentity NewPasskeyIdentity(string credentialId)
        => new(AuthSchema.Passkey, credentialId);
```

(Check the file's existing `New*Identity` helpers for the exact constructor form they use — `new(schema, value)` — and match it.)

- [ ] **Step 3: Central package versions**

In `Directory.Packages.props`, in alphabetical position:

```xml
    <PackageVersion Include="Fido2" Version="4.0.0" />
    <PackageVersion Include="System.Formats.Cbor" Version="10.0.0" />
```

In `src/dotnet/Users.Service/Users.Service.csproj`, in the `<ItemGroup>` with other `PackageReference`s:

```xml
    <PackageReference Include="Fido2" />
```

- [ ] **Step 4: Settings**

In `src/dotnet/Users.Service/Module/UsersSettings.cs`, after `NewAccountStatus`:

```csharp
    // Null = on everywhere except production; set explicitly to override
    public bool? IsPasskeyAuthEnabled { get; set; }
    // Empty = the public host of HostInfo.BaseUrl
    public string PasskeyRpId { get; set; } = "";
    // ';'-separated; empty = the origin of HostInfo.BaseUrl. Android app origins look like
    // android:apk-key-hash:<base64url(sha256(signing cert))>
    public string PasskeyOrigins { get; set; } = "";
    public TimeSpan PasskeyChallengeLifetime { get; set; } = TimeSpan.FromMinutes(2);
```

- [ ] **Step 5: DB entity**

Create `src/dotnet/Users.Service/Db/DbPasskey.cs`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Db;

[Table("Passkeys")]
[Index(nameof(UserId))]
public class DbPasskey : IHasId<string>
{
    [DbKey] public string Id { get; set; } = ""; // base64url credential id
    public string UserId { get; set; } = "";
    public byte[] UserHandle { get; set; } = [];
    public byte[] PublicKey { get; set; } = [];
    public long SignCount { get; set; }
    public Guid Aaguid { get; set; }
    public string Transports { get; set; } = "";
    public bool IsBackupEligible { get; set; }
    public bool IsBackedUp { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public DbPasskey() { }
    public DbPasskey(PasskeyCredential model) => UpdateFrom(model);

    public PasskeyCredential ToModel()
        => new(Id, new UserId(UserId)) {
            UserHandle = UserHandle,
            PublicKey = PublicKey,
            SignCount = (uint)SignCount,
            Aaguid = Aaguid,
            Transports = Transports,
            IsBackupEligible = IsBackupEligible,
            IsBackedUp = IsBackedUp,
            Name = Name,
            CreatedAt = CreatedAt.ToMoment(),
            LastUsedAt = LastUsedAt?.ToMoment(),
        };

    public void UpdateFrom(PasskeyCredential model)
    {
        Id = model.Id;
        UserId = model.UserId.Value;
        UserHandle = model.UserHandle;
        PublicKey = model.PublicKey;
        SignCount = model.SignCount;
        Aaguid = model.Aaguid;
        Transports = model.Transports;
        IsBackupEligible = model.IsBackupEligible;
        IsBackedUp = model.IsBackedUp;
        Name = model.Name;
        CreatedAt = model.CreatedAt.ToDateTime();
        LastUsedAt = model.LastUsedAt?.ToDateTime();
    }
}
```

`PasskeyCredential` is defined in Task 2; this file won't compile until then — do Steps 5–6 and Task 2 Step 3 before building. (`ToMoment()` / `ToDateTime()` are the existing `DateTime` ↔ `Moment` extensions used by `DbAccount`; check `DbAccount.cs` and use the same names.)

- [ ] **Step 6: DbContext**

In `src/dotnet/Users.Service/Db/UsersDbContext.cs`, add after `ChatUsages`:

```csharp
    public DbSet<DbPasskey> Passkeys { get; protected set; } = null!;
```

and in `OnModelCreating`, after the `userPresence` block:

```csharp
        var passkey = model.Entity<DbPasskey>();
        passkey.Property(e => e.Id).UseCollation("C");
        passkey.Property(e => e.UserId).UseCollation("C");
```

- [ ] **Step 7: Build and generate the migration**

After Task 2 Step 3 exists (`PasskeyCredential`):

```bash
dotnet build src/dotnet/Users.Service.Migration/Users.Service.Migration.csproj
./ef-migrations.cmd Users.Service add Add_Passkeys
```

Expected: a new `<timestamp>_Add_Passkeys.cs` creating table `passkeys` (snake_case) with an index on `user_id`, and `UsersDbContextModelSnapshot.cs` updated. Open the migration and confirm it contains only the new table (no drift on other tables). If it shows unrelated changes, stop and report.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/Core/AuthSchema.cs src/dotnet/Api/Users/UserIdentityExt.cs Directory.Packages.props src/dotnet/Users.Service src/dotnet/Users.Service.Migration
git commit -m "feat(auth): passkey schema, settings and Passkeys table"
```

---

### Task 2: Contracts — `Passkey`, `IPasskeyAuth`, `IPasskeysBackend`

**Files:**
- Create: `src/dotnet/Api/Users/Passkey.cs`
- Create: `src/dotnet/Api.Contracts/Users/IPasskeyAuth.cs`
- Create: `src/dotnet/Users.Contracts/IPasskeysBackend.cs`
- Modify: `src/dotnet/Api.Contracts/Module/ApiContractsModule.cs`

**Interfaces:**
- Produces everything below verbatim; Tasks 3–11 call these names.

- [ ] **Step 1: API record**

Create `src/dotnet/Api/Users/Passkey.cs`:

```csharp
namespace ActualChat.Users;

[DataContract, MessagePackObject]
public sealed partial record Passkey(
    [property: DataMember, Key(0)] string Id,
    [property: DataMember, Key(1)] string Name,
    [property: DataMember, Key(2)] Moment CreatedAt,
    [property: DataMember, Key(3)] Moment? LastUsedAt,
    [property: DataMember, Key(4)] bool IsSynced
);
```

- [ ] **Step 2: API contract**

Create `src/dotnet/Api.Contracts/Users/IPasskeyAuth.cs`:

```csharp
namespace ActualChat.Users;

/// <summary>
/// WebAuthn (passkey) registration and sign-in. Options and responses are the standard
/// WebAuthn JSON shapes, so every client - web, Android, Apple - speaks the same contract.
/// </summary>
public interface IPasskeyAuth : IComputeService
{
    [ComputeMethod]
    Task<bool> IsEnabled(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<string> GetRpId(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<Passkey>> ListOwn(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task<string> OnBeginRegistration(PasskeyAuth_BeginRegistration command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Passkey> OnCompleteRegistration(PasskeyAuth_CompleteRegistration command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<string> OnBeginSignIn(PasskeyAuth_BeginSignIn command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnCompleteSignIn(PasskeyAuth_CompleteSignIn command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRename(PasskeyAuth_Rename command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDelete(PasskeyAuth_Delete command, CancellationToken cancellationToken);
}

public enum PasskeyPurpose
{
    AddToAccount = 0,
    SignUp, // Reserved for passkey-first signup; refused until that ships
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeyAuth_BeginRegistration : ApiCommand<string>, INotDeduplicated
{
    [DataMember(Order = 2), Key(2)] public PasskeyPurpose Purpose { get; init; } = PasskeyPurpose.AddToAccount;
    [DataMember(Order = 3), Key(3)] public string? Name { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeyAuth_CompleteRegistration : ApiCommand<Passkey>
{
    [DataMember(Order = 2), Key(2)] public required string AttestationJson { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeyAuth_BeginSignIn : ApiCommand<string>, INotDeduplicated;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeyAuth_CompleteSignIn : ApiCommand<bool>
{
    [DataMember(Order = 2), Key(2)] public required string AssertionJson { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeyAuth_Rename : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Id { get; init; }
    [DataMember(Order = 3), Key(3)] public required string Name { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeyAuth_Delete : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Id { get; init; }
}
```

`INotDeduplicated` lives in `Core/Commands/ApiCommand.cs`; the two `Begin*` commands issue a fresh challenge every time, so dedup would return a stale one.

- [ ] **Step 3: Backend contract**

Create `src/dotnet/Users.Contracts/IPasskeysBackend.cs`:

```csharp
namespace ActualChat.Users;

public interface IPasskeysBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<PasskeyCredential?> Get(UserId userId, string id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<PasskeyCredential>> List(UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<PasskeyCredential?> OnChange(PasskeysBackend_Change command, CancellationToken cancellationToken);
}

/// <summary>
/// A stored passkey with its key material. <see cref="Passkey"/> is the user-facing projection.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record PasskeyCredential(
    [property: DataMember, Key(0)] string Id,
    [property: DataMember, Key(1)] UserId UserId
) {
    [DataMember, Key(2)] public byte[] UserHandle { get; init; } = [];
    [DataMember, Key(3)] public byte[] PublicKey { get; init; } = [];
    [DataMember, Key(4)] public uint SignCount { get; init; }
    [DataMember, Key(5)] public Guid Aaguid { get; init; }
    [DataMember, Key(6)] public string Transports { get; init; } = "";
    [DataMember, Key(7)] public bool IsBackupEligible { get; init; }
    [DataMember, Key(8)] public bool IsBackedUp { get; init; }
    [DataMember, Key(9)] public string Name { get; init; } = "";
    [DataMember, Key(10)] public Moment CreatedAt { get; init; }
    [DataMember, Key(11)] public Moment? LastUsedAt { get; init; }

    public Passkey ToPasskey()
        => new(Id, Name, CreatedAt, LastUsedAt, IsBackupEligible);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeysBackend_Change(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] string Id,
    [property: DataMember, Key(2)] Change<PasskeyCredential> Change
) : ICommand<PasskeyCredential?>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}
```

- [ ] **Step 4: Register the client**

In `src/dotnet/Api.Contracts/Module/ApiContractsModule.cs`, next to `fusion.AddClient<IPhoneAuth>();`:

```csharp
        fusion.AddClient<IPasskeyAuth>();
```

- [ ] **Step 5: Build**

```bash
dotnet build src/dotnet/Users.Service/Users.Service.csproj
```

Expected: success (Task 1's `DbPasskey` now resolves `PasskeyCredential`). Now run Task 1 Step 7 (migration) if not done yet.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api/Users/Passkey.cs src/dotnet/Api.Contracts src/dotnet/Users.Contracts src/dotnet/Users.Service.Migration
git commit -m "feat(auth): passkey API and backend contracts"
```

---

### Task 3: `PasskeysBackend` — storage, identity row, invalidation

**Files:**
- Create: `src/dotnet/Users.Service/Passkey/PasskeysBackend.cs`
- Modify: `src/dotnet/Users.Service/Module/UsersServiceModule.cs`
- Test: `tests/Users.IntegrationTests/PasskeysBackendTest.cs`

**Interfaces:**
- Consumes: `IPasskeysBackend`, `PasskeysBackend_Change`, `DbPasskey`, `AccountsBackend.Get` / `GetIdByUserIdentity`.
- Produces: a working backend where `Create` also inserts the `AccountIdentities` row and `Remove` deletes it; both invalidate `AccountsBackend.Get(userId)` and `GetIdByUserIdentity(identity)`.

- [ ] **Step 1: Write the failing test**

Create `tests/Users.IntegrationTests/PasskeysBackendTest.cs`:

```csharp
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class PasskeysBackendTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IPasskeysBackend Backend => AppHost.Services.GetRequiredService<IPasskeysBackend>();
    private IAccountsBackend AccountsBackend => AppHost.Services.GetRequiredService<IAccountsBackend>();

    [Fact]
    public async Task CreateShouldAddIdentityAndListThePasskey()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var credential = NewCredential(account.Id);

        // act
        await Commander.Call(new PasskeysBackend_Change(account.Id, credential.Id, Change.Create(credential)));

        // assert
        var listed = await Backend.List(account.Id, default);
        listed.Should().ContainSingle(x => x.Id == credential.Id);
        var identity = UserIdentityExt.NewPasskeyIdentity(credential.Id);
        (await AccountsBackend.GetIdByUserIdentity(identity, default)).Should().Be(account.Id);
        var full = await AccountsBackend.Get(account.Id, default);
        full!.Identities.Keys.Should().Contain(identity, "the passkey must be an account identity");
    }

    [Fact]
    public async Task RemoveShouldDropIdentityAndPasskey()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var credential = NewCredential(account.Id);
        await Commander.Call(new PasskeysBackend_Change(account.Id, credential.Id, Change.Create(credential)));

        // act
        await Commander.Call(new PasskeysBackend_Change(account.Id, credential.Id, Change.Remove<PasskeyCredential>()));

        // assert
        (await Backend.Get(account.Id, credential.Id, default)).Should().BeNull();
        (await Backend.List(account.Id, default)).Should().BeEmpty();
        var identity = UserIdentityExt.NewPasskeyIdentity(credential.Id);
        (await AccountsBackend.GetIdByUserIdentity(identity, default)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateShouldPersistCounterAndName()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        var credential = NewCredential(account.Id);
        await Commander.Call(new PasskeysBackend_Change(account.Id, credential.Id, Change.Create(credential)));
        var updated = credential with { SignCount = 7, Name = "Laptop", IsBackedUp = true };

        // act
        await Commander.Call(new PasskeysBackend_Change(account.Id, credential.Id, Change.Update(updated)));

        // assert
        var stored = await Backend.Get(account.Id, credential.Id, default);
        stored.Should().NotBeNull();
        stored!.SignCount.Should().Be(7);
        stored.Name.Should().Be("Laptop");
        stored.IsBackedUp.Should().BeTrue();
    }

    [Fact]
    public async Task CreateShouldRefuseCredentialOwnedByAnotherAccount()
    {
        // arrange
        await using var tester1 = AppHost.NewWebClientTester(Out);
        await using var tester2 = AppHost.NewWebClientTester(Out);
        var alice = await tester1.SignInAsUniqueAlice();
        var bob = await tester2.SignInAsUniqueBob();
        var credential = NewCredential(alice.Id);
        await Commander.Call(new PasskeysBackend_Change(alice.Id, credential.Id, Change.Create(credential)));

        // act
        var act = () => Commander.Call(
            new PasskeysBackend_Change(bob.Id, credential.Id, Change.Create(credential with { UserId = bob.Id })));

        // assert
        await act.Should().ThrowAsync<Exception>("a credential id belongs to exactly one account");
    }

    private static PasskeyCredential NewCredential(UserId userId)
        => new(UniqueNames.Random(32), userId) {
            UserHandle = RandomNumberGenerator.GetBytes(32),
            PublicKey = RandomNumberGenerator.GetBytes(77),
            Aaguid = Guid.NewGuid(),
            Transports = "internal",
            IsBackupEligible = true,
            Name = "Test passkey",
            CreatedAt = Moment.Now,
        };
}
```

Add `using System.Security.Cryptography;` at the top. (`SignInAsUniqueBob` — check `tests/Testing.Host/UserOperations.cs` for the exact Bob helper name and use it.)

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test tests/Users.IntegrationTests --filter "FullyQualifiedName~PasskeysBackendTest"
```

Expected: FAIL — `IPasskeysBackend` isn't registered.

- [ ] **Step 3: Implement the backend**

Create `src/dotnet/Users.Service/Passkey/PasskeysBackend.cs`:

```csharp
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Passkey;

public class PasskeysBackend(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IPasskeysBackend
{
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();

    // [ComputeMethod]
    public virtual async Task<PasskeyCredential?> Get(UserId userId, string id, CancellationToken cancellationToken)
    {
        if (userId.IsNone || id.IsNullOrEmpty())
            return null;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbPasskey = await dbContext.Passkeys
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId.Value, cancellationToken)
            .ConfigureAwait(false);
        return dbPasskey?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<PasskeyCredential>> List(UserId userId, CancellationToken cancellationToken)
    {
        if (userId.IsNone)
            return ApiArray<PasskeyCredential>.Empty;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbPasskeys = await dbContext.Passkeys
            .Where(x => x.UserId == userId.Value)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbPasskeys.Select(x => x.ToModel()).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<PasskeyCredential?> OnChange(
        PasskeysBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (userId, id, change) = command;
        var identity = UserIdentityExt.NewPasskeyIdentity(id);
        if (Invalidation.IsActive) {
            _ = Get(userId, id, default);
            _ = List(userId, default);
            if (change.Kind != ChangeKind.Update) {
                _ = AccountsBackend.Get(userId, default);
                _ = AccountsBackend.GetIdByUserIdentity(identity, default);
            }
            return default!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        await dbContext.Accounts.Lock(userId.Value, cancellationToken).ConfigureAwait(false);

        DbPasskey? dbPasskey;
        if (change.IsCreate(out var credential)) {
            if (credential.Id != id || credential.UserId != userId)
                throw StandardError.Constraint("Passkey id or owner mismatch.");

            var ownerId = await dbContext.GetUserIdByIdentity(identity, true, cancellationToken).ConfigureAwait(false);
            if (ownerId is not null)
                throw StandardError.Unauthorized("This passkey is already registered.");

            dbPasskey = new DbPasskey(credential);
            dbContext.Passkeys.Add(dbPasskey);
            dbContext.AccountIdentities.Add(new DbAccountIdentity {
                Id = identity.Id,
                DbAccountId = userId.Value,
                Secret = "",
            });
        }
        else if (change.IsUpdate(out credential)) {
            dbPasskey = await dbContext.Passkeys
                .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId.Value, cancellationToken)
                .ConfigureAwait(false);
            dbPasskey = dbPasskey.Require();
            dbPasskey.UpdateFrom(credential with { Id = id, UserId = userId });
        }
        else {
            dbPasskey = await dbContext.Passkeys
                .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (dbPasskey is null)
                return null;

            dbContext.Passkeys.Remove(dbPasskey);
            var dbIdentity = await dbContext.AccountIdentities
                .FirstOrDefaultAsync(x => x.Id == identity.Id, cancellationToken)
                .ConfigureAwait(false);
            if (dbIdentity is not null)
                dbContext.AccountIdentities.Remove(dbIdentity);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return change.IsRemove() ? null : dbPasskey.ToModel();
    }
}
```

Check `dbContext.Accounts.Lock(...)` is the same helper `AccountsBackend.OnUpdate` uses (it is, `await dbContext.Accounts.Lock(userId, cancellationToken)`), and that `UserId.IsNone` is the right emptiness check on `UserId` (grep `IsNone` in `src/dotnet/Api/Identifiers/UserId.cs`).

- [ ] **Step 4: Register it**

In `src/dotnet/Users.Service/Module/UsersServiceModule.cs`, next to the `// PhoneAuth` block (find where backends are registered — e.g. `rpcHost.AddBackend<IAccountsBackend, AccountsBackend>()` — and match that form):

```csharp
        // Passkeys
        rpcHost.AddBackend<IPasskeysBackend, PasskeysBackend>();
```

Add `using ActualChat.Users.Passkey;` if the file doesn't already import the namespace.

- [ ] **Step 5: Run the tests**

```bash
dotnet test tests/Users.IntegrationTests --filter "FullyQualifiedName~PasskeysBackendTest"
```

Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Users.Service tests/Users.IntegrationTests/PasskeysBackendTest.cs
git commit -m "feat(auth): PasskeysBackend with identity row and invalidation"
```

---

### Task 4: Software authenticator test helper

**Files:**
- Create: `tests/Testing/Passkeys/SoftwareAuthenticator.cs`
- Modify: `tests/Testing/Testing.csproj`
- Test: `tests/Users.IntegrationTests/SoftwareAuthenticatorTest.cs`

**Interfaces:**
- Produces: `SoftwareAuthenticator(string rpId, string origin)` with `CredentialId : string` (base64url), `SignCount`, `IsBackupEligible`, `IsBackedUp`, `UserHandle : byte[]?`; `string CreateAttestationJson(string creationOptionsJson)`; `string CreateAssertionJson(string assertionOptionsJson)`; `static string Base64Url(byte[])`.

- [ ] **Step 1: Package reference**

In `tests/Testing/Testing.csproj`, in the `PackageReference` item group:

```xml
    <PackageReference Include="System.Formats.Cbor" />
```

- [ ] **Step 2: Write the helper**

Create `tests/Testing/Passkeys/SoftwareAuthenticator.cs`:

```csharp
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ActualChat.Testing.Passkeys;

/// <summary>
/// A P-256 WebAuthn authenticator in software: produces real attestation ("none" format) and
/// assertion responses for the options JSON the server hands out, so tests exercise Fido2NetLib's
/// verification rather than mocking it.
/// </summary>
public sealed class SoftwareAuthenticator(string rpId, string origin) : IDisposable
{
    private const byte FlagUserPresent = 0x01;
    private const byte FlagUserVerified = 0x04;
    private const byte FlagBackupEligible = 0x08;
    private const byte FlagBackedUp = 0x10;
    private const byte FlagAttestedCredentialData = 0x40;

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _credentialId = RandomNumberGenerator.GetBytes(32);

    public static readonly Guid Aaguid = new("0102030405060708090a0b0c0d0e0f10");

    public string CredentialId => Base64Url(_credentialId);
    public byte[]? UserHandle { get; private set; }
    public uint SignCount { get; set; }
    public bool IsBackupEligible { get; init; } = true;
    public bool IsBackedUp { get; init; } = true;
    public string Origin { get; set; } = origin;

    public void Dispose()
        => _key.Dispose();

    public string CreateAttestationJson(string creationOptionsJson)
    {
        using var options = JsonDocument.Parse(creationOptionsJson);
        var challenge = options.RootElement.GetProperty("challenge").GetString()!;
        UserHandle = Base64Url.DecodeFromChars(options.RootElement.GetProperty("user").GetProperty("id").GetString()!);
        var clientDataJson = ClientDataJson("webauthn.create", challenge);
        var authData = AuthData(withCredential: true);
        var attestationObject = AttestationObject(authData);
        return JsonSerializer.Serialize(new {
            id = CredentialId,
            rawId = CredentialId,
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new {
                clientDataJSON = Base64Url(clientDataJson),
                attestationObject = Base64Url(attestationObject),
                transports = new[] { "internal" },
            },
        });
    }

    public string CreateAssertionJson(string assertionOptionsJson)
    {
        using var options = JsonDocument.Parse(assertionOptionsJson);
        var challenge = options.RootElement.GetProperty("challenge").GetString()!;
        var clientDataJson = ClientDataJson("webauthn.get", challenge);
        var authData = AuthData(withCredential: false);
        var signed = authData.Concat(SHA256.HashData(clientDataJson)).ToArray();
        var signature = _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return JsonSerializer.Serialize(new {
            id = CredentialId,
            rawId = CredentialId,
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new {
                clientDataJSON = Base64Url(clientDataJson),
                authenticatorData = Base64Url(authData),
                signature = Base64Url(signature),
                userHandle = UserHandle is null ? null : Base64Url(UserHandle),
            },
        });
    }

    public static string Base64Url(byte[] bytes)
        => System.Buffers.Text.Base64Url.EncodeToString(bytes);

    // Private methods

    private byte[] ClientDataJson(string type, string challenge)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            type,
            challenge,
            origin = Origin,
            crossOrigin = false,
        }));

    private byte[] AuthData(bool withCredential)
    {
        var flags = (byte)(FlagUserPresent | FlagUserVerified);
        if (IsBackupEligible)
            flags |= FlagBackupEligible;
        if (IsBackedUp)
            flags |= FlagBackedUp;
        if (withCredential)
            flags |= FlagAttestedCredentialData;

        var counter = ++SignCount;
        var buffer = new List<byte>(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        buffer.Add(flags);
        buffer.AddRange([(byte)(counter >> 24), (byte)(counter >> 16), (byte)(counter >> 8), (byte)counter]);
        if (!withCredential)
            return buffer.ToArray();

        buffer.AddRange(Aaguid.ToByteArray(bigEndian: true));
        buffer.AddRange([(byte)(_credentialId.Length >> 8), (byte)_credentialId.Length]);
        buffer.AddRange(_credentialId);
        buffer.AddRange(CoseKey());
        return buffer.ToArray();
    }

    private byte[] CoseKey()
    {
        var q = _key.ExportParameters(false).Q;
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(5);
        writer.WriteInt32(1); writer.WriteInt32(2);   // kty: EC2
        writer.WriteInt32(3); writer.WriteInt32(-7);  // alg: ES256
        writer.WriteInt32(-1); writer.WriteInt32(1);  // crv: P-256
        writer.WriteInt32(-2); writer.WriteByteString(Pad32(q.X!));
        writer.WriteInt32(-3); writer.WriteByteString(Pad32(q.Y!));
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] AttestationObject(byte[] authData)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteTextString("fmt"); writer.WriteTextString("none");
        writer.WriteTextString("attStmt"); writer.WriteStartMap(0); writer.WriteEndMap();
        writer.WriteTextString("authData"); writer.WriteByteString(authData);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] Pad32(byte[] value)
        => value.Length >= 32 ? value : new byte[32 - value.Length].Concat(value).ToArray();
}
```

If `Guid.ToByteArray(bool bigEndian)` isn't available on the test TFM, replace with the manual reorder of the first three fields (bytes 0-3, 4-5, 6-7 reversed). `CborConformanceMode.Ctap2Canonical` sorts map keys, which is what authenticators emit; keep it.

- [ ] **Step 3: Write a self-test through Fido2NetLib**

Create `tests/Users.IntegrationTests/SoftwareAuthenticatorTest.cs`:

```csharp
using System.Text.Json;
using ActualChat.Testing.Passkeys;
using Fido2NetLib;
using Fido2NetLib.Objects;

namespace ActualChat.Users.IntegrationTests;

public class SoftwareAuthenticatorTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string RpId = "localhost";
    private const string Origin = "https://localhost";

    [Fact]
    public async Task AttestationAndAssertionShouldVerifyWithFido2()
    {
        // arrange
        var fido2 = new Fido2(new Fido2Configuration {
            ServerDomain = RpId,
            ServerName = "Test",
            Origins = new HashSet<string> { Origin },
        }, null);
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var user = new Fido2User { Id = RandomNumberGenerator.GetBytes(32), Name = "alice", DisplayName = "Alice" };
        var createOptions = fido2.RequestNewCredential(new RequestNewCredentialParams {
            User = user,
            AuthenticatorSelection = new AuthenticatorSelection {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });

        // act
        var attestationJson = authenticator.CreateAttestationJson(createOptions.ToJson());
        var attestation = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(attestationJson)!;
        var registered = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams {
            AttestationResponse = attestation,
            OriginalOptions = createOptions,
            IsCredentialIdUniqueToUserCallback = (_, _) => Task.FromResult(true),
        }, CancellationToken.None);

        var assertionOptions = fido2.GetAssertionOptions(new GetAssertionOptionsParams {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Required,
        });
        var assertionJson = authenticator.CreateAssertionJson(assertionOptions.ToJson());
        var assertion = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(assertionJson)!;
        var verified = await fido2.MakeAssertionAsync(new MakeAssertionParams {
            AssertionResponse = assertion,
            OriginalOptions = assertionOptions,
            StoredPublicKey = registered.PublicKey,
            StoredSignatureCounter = registered.SignCount,
            IsUserHandleOwnerOfCredentialIdCallback = (p, _) => Task.FromResult(p.UserHandle.SequenceEqual(user.Id)),
        }, CancellationToken.None);

        // assert
        registered.Id.Should().Equal(Base64Url.DecodeFromChars(authenticator.CredentialId));
        registered.IsBackupEligible.Should().BeTrue();
        registered.AaGuid.Should().Be(SoftwareAuthenticator.Aaguid);
        verified.SignCount.Should().Be(2, "the second ceremony bumps the counter");
        verified.IsBackedUp.Should().BeTrue();
    }
}
```

Add `using System.Buffers.Text;` and `using System.Security.Cryptography;`. `TestBase` is `ActualChat.Testing.TestBase`; if its constructor needs different arguments, follow another plain (non-AppHost) test in the same project, e.g. `AppleClientSecretGeneratorMock`'s siblings, or use `SharedAppHostTestBase` like the others — this test doesn't need the host.

- [ ] **Step 4: Run it**

```bash
dotnet test tests/Users.IntegrationTests --filter "FullyQualifiedName~SoftwareAuthenticatorTest"
```

Expected: PASS. If Fido2 throws `Fido2VerificationException`, its `Message` says which check failed (challenge, origin, rpIdHash, flags, attestation format, signature); fix the helper, not the test.

- [ ] **Step 5: Commit**

```bash
git add tests/Testing tests/Users.IntegrationTests/SoftwareAuthenticatorTest.cs
git commit -m "test(auth): software WebAuthn authenticator for passkey tests"
```

---

### Task 5: `PasskeyAuth` — ceremonies, challenges, sign-in, list, rename, delete

**Files:**
- Create: `src/dotnet/Users.Service/Passkey/PasskeyAuth.cs`
- Create: `src/dotnet/Users.Service/Passkey/PasskeyNames.cs`
- Modify: `src/dotnet/Users.Service/Module/UsersServiceModule.cs`
- Modify: `tests/Users.IntegrationTests/Collections/UserCollection.cs`
- Test: `tests/Users.IntegrationTests/PasskeyAuthTest.cs`

**Interfaces:**
- Consumes: `IPasskeysBackend`, `AccountsBackend_SignIn`, `RateLimitPolicy`, `RedisDb<UsersDbContext>`, `SoftwareAuthenticator`.
- Produces: `IPasskeyAuth` implementation; `IFido2` singleton.

- [ ] **Step 1: Configure the test host**

In `tests/Users.IntegrationTests/Collections/UserCollection.cs`, extend `ConfigureHost`:

```csharp
        ConfigureHost = (_, cfg) => {
            cfg.AddInMemory<UsersSettings>(
                (x => x.AppleAppId, "com.test.app"),
                (x => x.PasskeyRpId, "localhost"),
                (x => x.PasskeyOrigins, "https://localhost"));
        },
```

(If `AddInMemory` takes one pair per call, call it three times.)

- [ ] **Step 2: Write the failing tests**

Create `tests/Users.IntegrationTests/PasskeyAuthTest.cs`:

```csharp
using ActualChat.Testing.Host;
using ActualChat.Testing.Passkeys;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class PasskeyAuthTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string RpId = "localhost";
    private const string Origin = "https://localhost";

    private IPasskeyAuth PasskeyAuth => AppHost.Services.GetRequiredService<IPasskeyAuth>();
    private IAccounts Accounts => AppHost.Services.GetRequiredService<IAccounts>();

    [Fact]
    public async Task IsEnabledShouldBeTrueOutsideProduction()
        => (await PasskeyAuth.IsEnabled(default)).Should().BeTrue();

    [Fact]
    public async Task GetRpIdShouldComeFromSettings()
        => (await PasskeyAuth.GetRpId(default)).Should().Be(RpId);

    [Fact]
    public async Task RegisterThenSignInShouldLandOnTheSameAccount()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);

        // act
        var passkey = await Register(tester.Session, authenticator);
        var signInSession = await NewSession();
        var isSignedIn = await SignIn(signInSession, authenticator);

        // assert
        isSignedIn.Should().BeTrue();
        passkey.Id.Should().Be(authenticator.CredentialId);
        passkey.IsSynced.Should().BeTrue();
        var signedIn = await Accounts.GetOwn(signInSession, default);
        signedIn.Id.Should().Be(account.Id);
        var listed = await PasskeyAuth.ListOwn(tester.Session, default);
        listed.Should().ContainSingle(x => x.Id == passkey.Id && x.LastUsedAt != null);
    }

    [Fact]
    public async Task BeginRegistrationShouldRefuseGuests()
    {
        // arrange
        var session = await NewSession();

        // act
        var act = () => Commander.Call(new PasskeyAuth_BeginRegistration { Session = session });

        // assert
        await act.Should().ThrowAsync<Exception>("only a signed-in user can add a passkey");
    }

    [Fact]
    public async Task BeginRegistrationShouldRefuseSignUpPurposeForNow()
    {
        // arrange
        var session = await NewSession();

        // act
        var act = () => Commander.Call(
            new PasskeyAuth_BeginRegistration { Session = session, Purpose = PasskeyPurpose.SignUp });

        // assert
        await act.Should().ThrowAsync<Exception>("passkey-first signup ships in a later PR");
    }

    [Fact]
    public async Task ChallengeShouldBeSingleUse()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var options = await Commander.Call(new PasskeyAuth_BeginRegistration { Session = tester.Session });
        var attestation = authenticator.CreateAttestationJson(options);
        await Commander.Call(new PasskeyAuth_CompleteRegistration { Session = tester.Session, AttestationJson = attestation });

        // act
        var act = () => Commander.Call(
            new PasskeyAuth_CompleteRegistration { Session = tester.Session, AttestationJson = attestation });

        // assert
        await act.Should().ThrowAsync<Exception>("the challenge is consumed by the first completion");
    }

    [Fact]
    public async Task SignInShouldRejectWrongOrigin()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        await Register(tester.Session, authenticator);
        var session = await NewSession();
        var options = await Commander.Call(new PasskeyAuth_BeginSignIn { Session = session });
        // The helper signs clientDataJSON as-is, so a foreign origin is a real, well-signed assertion
        authenticator.Origin = "https://evil.example";

        // act
        var act = () => Commander.Call(new PasskeyAuth_CompleteSignIn {
            Session = session,
            AssertionJson = authenticator.CreateAssertionJson(options),
        });

        // assert
        await act.Should().ThrowAsync<Exception>("an origin outside the allow-list must be refused");
        (await Accounts.GetOwn(session, default)).IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task SignInShouldRejectNonMonotonicCounter()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        await Register(tester.Session, authenticator);
        (await SignIn(await NewSession(), authenticator)).Should().BeTrue();
        authenticator.SignCount = 0; // a cloned authenticator replays an old counter
        var replaySession = await NewSession();

        // act
        var act = () => SignIn(replaySession, authenticator);

        // assert
        await act.Should().ThrowAsync<Exception>("a counter that doesn't advance signals a cloned key");
    }

    [Fact]
    public async Task SignInWithUnknownCredentialShouldFail()
    {
        // arrange
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var session = await NewSession();
        var options = await Commander.Call(new PasskeyAuth_BeginSignIn { Session = session });

        // act
        var act = () => Commander.Call(new PasskeyAuth_CompleteSignIn {
            Session = session,
            AssertionJson = authenticator.CreateAssertionJson(options),
        });

        // assert
        await act.Should().ThrowAsync<Exception>().WithMessage("*isn't linked to an account*");
    }

    [Fact]
    public async Task SecondPasskeyShouldReuseTheUserHandle()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var first = new SoftwareAuthenticator(RpId, Origin);
        using var second = new SoftwareAuthenticator(RpId, Origin);

        // act
        await Register(tester.Session, first);
        await Register(tester.Session, second);

        // assert
        second.UserHandle.Should().Equal(first.UserHandle, "Apple and Google dedupe passkeys by RP + user.id");
    }

    [Fact]
    public async Task RenameShouldChangeTheName()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var passkey = await Register(tester.Session, authenticator);

        // act
        await Commander.Call(new PasskeyAuth_Rename { Session = tester.Session, Id = passkey.Id, Name = "Work laptop" });

        // assert
        var listed = await PasskeyAuth.ListOwn(tester.Session, default);
        listed.Single().Name.Should().Be("Work laptop");
    }

    [Fact]
    public async Task DeleteShouldRemoveThePasskeyWhenAnotherIdentityExists()
    {
        // arrange — SignInAsUniqueAlice creates an account with a non-passkey identity
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var passkey = await Register(tester.Session, authenticator);

        // act
        await Commander.Call(new PasskeyAuth_Delete { Session = tester.Session, Id = passkey.Id });

        // assert
        (await PasskeyAuth.ListOwn(tester.Session, default)).Should().BeEmpty();
        (await SignIn(await NewSession(), authenticator)).Should().BeFalse("a deleted passkey can't sign in");
    }

    [Fact]
    public async Task DeleteShouldRefuseTheLastPasskeyWithoutAnotherIdentity()
    {
        // arrange — an account whose only identity is the passkey
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsUniqueAlice();
        using var authenticator = new SoftwareAuthenticator(RpId, Origin);
        var passkey = await Register(tester.Session, authenticator);
        await RemoveNonPasskeyIdentities(tester.Session);

        // act
        var act = () => Commander.Call(new PasskeyAuth_Delete { Session = tester.Session, Id = passkey.Id });

        // assert
        await act.Should().ThrowAsync<Exception>("deleting the only way in would lock the account");
    }

    // Helpers

    private async Task<Passkey> Register(Session session, SoftwareAuthenticator authenticator)
    {
        var options = await Commander.Call(new PasskeyAuth_BeginRegistration { Session = session });
        var attestation = authenticator.CreateAttestationJson(options);
        return await Commander.Call(
            new PasskeyAuth_CompleteRegistration { Session = session, AttestationJson = attestation });
    }

    private async Task<bool> SignIn(Session session, SoftwareAuthenticator authenticator)
    {
        var options = await Commander.Call(new PasskeyAuth_BeginSignIn { Session = session });
        var assertion = authenticator.CreateAssertionJson(options);
        try {
            return await Commander.Call(
                new PasskeyAuth_CompleteSignIn { Session = session, AssertionJson = assertion });
        }
        catch (Exception e) when (e.Message.Contains("isn't linked to an account")) {
            return false;
        }
    }

    private async Task<Session> NewSession()
    {
        var session = Session.New();
        await Commander.Call(new SessionsBackend_Upsert(session));
        return session;
    }

    private async Task RemoveNonPasskeyIdentities(Session session)
    {
        var account = await Accounts.GetOwn(session, default);
        var dbHub = AppHost.Services.GetRequiredService<ActualLab.Fusion.EntityFramework.DbHub<Db.UsersDbContext>>();
        var dbContext = await dbHub.CreateDbContext(default);
        await using var _ = dbContext;
        var rows = dbContext.AccountIdentities.Where(x => x.DbAccountId == account.Id.Value && !x.Id.StartsWith("passkey/"));
        dbContext.AccountIdentities.RemoveRange(rows);
        await dbContext.SaveChangesAsync();
        var accountsBackend = AppHost.Services.GetRequiredService<IAccountsBackend>();
        // Direct DB edit: drop the cached account so the delete guard sees the real identity set
        using (Invalidation.Begin())
            _ = accountsBackend.Get(account.Id, default);
    }
}
```

Note the identity id prefix: `UserIdentity.Id` is `"<schema>/<value>"` — check `UserIdentity.ParseId` in `src/dotnet/Api/Users/UserIdentity.cs` and adjust the `StartsWith` literal to whatever separator it uses.

- [ ] **Step 3: Run to verify they fail**

```bash
dotnet test tests/Users.IntegrationTests --filter "FullyQualifiedName~PasskeyAuthTest"
```

Expected: FAIL — `IPasskeyAuth` not registered.

- [ ] **Step 4: Default-name table**

Create `src/dotnet/Users.Service/Passkey/PasskeyNames.cs`:

```csharp
namespace ActualChat.Users.Passkey;

/// <summary>
/// Default passkey names by authenticator AAGUID (passkeydeveloper/passkey-authenticator-aaguids).
/// </summary>
public static class PasskeyNames
{
    private static readonly Dictionary<Guid, string> ByAaguid = new() {
        [new("fbfc3007-154e-4ecc-8c0b-6e020557d7bd")] = "iCloud Keychain",
        [new("ea9b8d66-4d01-1d21-3ce4-b6b48cb575d4")] = "Google Password Manager",
        [new("08987058-cadc-4b81-b6e1-30de50dcbe96")] = "Windows Hello",
        [new("9ddd1817-af5a-4672-a2b9-3e3dd95000a9")] = "Windows Hello",
        [new("6028b017-b1d4-4c02-b4b3-afcdafc96bb2")] = "Windows Hello",
        [new("adce0002-35bc-c60a-648b-0b25f1f05503")] = "Chrome on Mac",
        [new("bada5566-a7aa-401f-bd96-45619a55120d")] = "1Password",
        [new("d548826e-79b4-db40-a3d8-11116f7e8349")] = "Bitwarden",
        [new("531126d6-e717-415c-9320-3d9aa6981239")] = "Dashlane",
        [new("0ea242b4-43c4-4a1b-8b17-dd6d0b6baec6")] = "Keeper",
        [new("b84e4048-15dc-4dd0-8640-f4f60813c8af")] = "NordPass",
        [new("f3809540-7f14-49c1-a8b3-8f813b225541")] = "Enpass",
        [new("50726f74-6f6e-5061-7373-50726f746f6e")] = "Proton Pass",
        [new("53414d53-554e-4700-0000-000000000000")] = "Samsung Pass",
    };

    public static string GetDefault(Guid aaguid, string transports)
    {
        if (ByAaguid.TryGetValue(aaguid, out var name))
            return name;

        var isRoaming = transports.Contains("usb") || transports.Contains("nfc") || transports.Contains("ble");
        return isRoaming ? "Security key" : "Passkey";
    }
}
```

- [ ] **Step 5: Implement the service**

Create `src/dotnet/Users.Service/Passkey/PasskeyAuth.cs`:

```csharp
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using ActualChat.Hashing;
using ActualChat.Resilience;
using ActualChat.Rpc;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Redis;
using ActualLab.Rpc.Infrastructure;
using Fido2NetLib;
using Fido2NetLib.Objects;

namespace ActualChat.Users.Passkey;

public class PasskeyAuth : DbServiceBase<UsersDbContext>, IPasskeyAuth
{
    private const string CreateChallengeKeyPrefix = ".PasskeyChallenge:create:";
    private const string GetChallengeKeyPrefix = ".PasskeyChallenge:get:";
    private const int UserHandleLength = 32;

    private UsersSettings Settings { get; }
    private HostInfo HostInfo { get; }
    private IFido2 Fido2 { get; }
    private RateLimitPolicy RateLimitPolicy { get; }
    private RedisDb<UsersDbContext> RedisDb { get; }
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private IPasskeysBackend PasskeysBackend => field ??= Services.GetRequiredService<IPasskeysBackend>();

    public PasskeyAuth(IServiceProvider services) : base(services)
    {
        Settings = services.GetRequiredService<UsersSettings>();
        HostInfo = services.HostInfo();
        Fido2 = services.GetRequiredService<IFido2>();
        RateLimitPolicy = services.GetRequiredService<RateLimitPolicy>();
        RedisDb = services.GetRequiredService<RedisDb<UsersDbContext>>();
    }

    // [ComputeMethod]
    public virtual Task<bool> IsEnabled(CancellationToken cancellationToken)
        => Task.FromResult(Settings.IsPasskeyAuthEnabled ?? !HostInfo.IsProductionInstance);

    // [ComputeMethod]
    public virtual Task<string> GetRpId(CancellationToken cancellationToken)
        => Task.FromResult(Settings.GetPasskeyRpId(HostInfo));

    // [ComputeMethod]
    public virtual async Task<ApiArray<Passkey>> ListOwn(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuest)
            return ApiArray<Passkey>.Empty;

        var credentials = await PasskeysBackend.List(account.Id, cancellationToken).ConfigureAwait(false);
        return credentials.Select(x => x.ToPasskey()).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<string> OnBeginRegistration(
        PasskeyAuth_BeginRegistration command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!;

        var session = command.Session;
        await RequireEnabled(cancellationToken).ConfigureAwait(false);
        await CheckRateLimit(nameof(OnBeginRegistration), session, cancellationToken).ConfigureAwait(false);
        if (command.Purpose != PasskeyPurpose.AddToAccount)
            throw StandardError.NotSupported("Creating an account with a passkey isn't available yet.");

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);
        var existing = await PasskeysBackend.List(account.Id, cancellationToken).ConfigureAwait(false);
        var userHandle = existing.Count > 0
            ? existing[0].UserHandle
            : RandomNumberGenerator.GetBytes(UserHandleLength);
        var user = new Fido2User {
            Id = userHandle,
            Name = account.Email.IsNullOrEmpty() ? account.Name : account.Email,
            DisplayName = account.Name,
        };
        var options = Fido2.RequestNewCredential(new RequestNewCredentialParams {
            User = user,
            ExcludeCredentials = existing
                .Select(x => new PublicKeyCredentialDescriptor(Base64Url.DecodeFromChars(x.Id)))
                .ToList(),
            AuthenticatorSelection = new AuthenticatorSelection {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });
        var json = options.ToJson();
        await StoreChallenge(CreateChallengeKeyPrefix, session, json, cancellationToken).ConfigureAwait(false);
        return json;
    }

    // [CommandHandler]
    public virtual async Task<Passkey> OnCompleteRegistration(
        PasskeyAuth_CompleteRegistration command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!;

        var session = command.Session;
        await RequireEnabled(cancellationToken).ConfigureAwait(false);
        await CheckRateLimit(nameof(OnCompleteRegistration), session, cancellationToken).ConfigureAwait(false);
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);

        var optionsJson = await ConsumeChallenge(CreateChallengeKeyPrefix, session, cancellationToken).ConfigureAwait(false);
        var options = CredentialCreateOptions.FromJson(optionsJson);
        var attestation = Deserialize<AuthenticatorAttestationRawResponse>(command.AttestationJson);
        RegisteredPublicKeyCredential registered;
        try {
            registered = await Fido2.MakeNewCredentialAsync(new MakeNewCredentialParams {
                AttestationResponse = attestation,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = async (p, ct) => {
                    var identity = UserIdentityExt.NewPasskeyIdentity(Base64Url.EncodeToString(p.CredentialId));
                    var ownerId = await AccountsBackend.GetIdByUserIdentity(identity, ct).ConfigureAwait(false);
                    return ownerId is null;
                },
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Fido2VerificationException e) {
            Log.LogWarning(e, "Passkey registration failed: {Code}", e.Code);
            throw StandardError.Unauthorized("This passkey couldn't be verified. Please try again.");
        }

        var transports = string.Join(',', (registered.Transports ?? []).Select(x => x.ToString().ToLower()));
        var credential = new PasskeyCredential(Base64Url.EncodeToString(registered.Id), account.Id) {
            UserHandle = options.User.Id,
            PublicKey = registered.PublicKey,
            SignCount = registered.SignCount,
            Aaguid = registered.AaGuid,
            Transports = transports,
            IsBackupEligible = registered.IsBackupEligible,
            IsBackedUp = registered.IsBackedUp,
            Name = command.Name.IsNullOrWhiteSpace()
                ? PasskeyNames.GetDefault(registered.AaGuid, transports)
                : command.Name.Trim(),
            CreatedAt = Clocks.SystemClock.Now,
        };
        var changeCommand = new PasskeysBackend_Change(account.Id, credential.Id, Change.Create(credential));
        var stored = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
        return stored!.ToPasskey();
    }

    // [CommandHandler]
    public virtual async Task<string> OnBeginSignIn(PasskeyAuth_BeginSignIn command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!;

        var session = command.Session;
        await RequireEnabled(cancellationToken).ConfigureAwait(false);
        await CheckRateLimit(nameof(OnBeginSignIn), session, cancellationToken).ConfigureAwait(false);
        var options = Fido2.GetAssertionOptions(new GetAssertionOptionsParams {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Required,
        });
        var json = options.ToJson();
        await StoreChallenge(GetChallengeKeyPrefix, session, json, cancellationToken).ConfigureAwait(false);
        return json;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnCompleteSignIn(PasskeyAuth_CompleteSignIn command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return false;

        var session = command.Session;
        await RequireEnabled(cancellationToken).ConfigureAwait(false);
        await CheckRateLimit(nameof(OnCompleteSignIn), session, cancellationToken).ConfigureAwait(false);

        var optionsJson = await ConsumeChallenge(GetChallengeKeyPrefix, session, cancellationToken).ConfigureAwait(false);
        var options = AssertionOptions.FromJson(optionsJson);
        var assertion = Deserialize<AuthenticatorAssertionRawResponse>(command.AssertionJson);
        var credentialId = Base64Url.EncodeToString(assertion.RawId);
        var identity = UserIdentityExt.NewPasskeyIdentity(credentialId);
        var userId = await AccountsBackend.GetIdByUserIdentity(identity, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.NotFound("This passkey isn't linked to an account.");
        var stored = await PasskeysBackend.Get(userId, credentialId, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.NotFound("This passkey isn't linked to an account.");

        VerifyAssertionResult verified;
        try {
            verified = await Fido2.MakeAssertionAsync(new MakeAssertionParams {
                AssertionResponse = assertion,
                OriginalOptions = options,
                StoredPublicKey = stored.PublicKey,
                StoredSignatureCounter = stored.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (p, _)
                    => Task.FromResult(p.UserHandle.AsSpan().SequenceEqual(stored.UserHandle)),
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Fido2VerificationException e) {
            Log.LogWarning(e, "Passkey sign-in failed: {Code}", e.Code);
            throw StandardError.Unauthorized("This passkey couldn't be verified. Please try again.");
        }

        var used = stored with {
            SignCount = verified.SignCount,
            IsBackedUp = verified.IsBackedUp,
            LastUsedAt = Clocks.SystemClock.Now,
        };
        await Commander.Call(new PasskeysBackend_Change(userId, credentialId, Change.Update(used)), true, cancellationToken)
            .ConfigureAwait(false);

        var identities = new ApiMap<UserIdentity, string>().With(identity, "");
        var signInCommand = new AccountsBackend_SignIn(session, identity, identities, new ApiMap<string, string>());
        await Commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // [CommandHandler]
    public virtual async Task OnRename(PasskeyAuth_Rename command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, id, name) = (command.Session, command.Id, command.Name.Trim());
        if (name.IsNullOrEmpty() || name.Length > 64)
            throw StandardError.Constraint("Passkey name must be 1 to 64 characters long.");

        var (account, stored) = await GetOwnPasskey(session, id, cancellationToken).ConfigureAwait(false);
        var changeCommand = new PasskeysBackend_Change(account.Id, id, Change.Update(stored with { Name = name }));
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDelete(PasskeyAuth_Delete command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, id) = (command.Session, command.Id);
        var (account, _) = await GetOwnPasskey(session, id, cancellationToken).ConfigureAwait(false);
        var passkeys = await PasskeysBackend.List(account.Id, cancellationToken).ConfigureAwait(false);
        var hasOtherIdentity = account.Identities.Keys.Any(x => x.Schema != AuthSchema.Passkey);
        if (passkeys.Count == 1 && !hasOtherIdentity)
            throw StandardError.Constraint(
                "This is the only way to sign in to your account. Add a phone or email first.");

        var changeCommand = new PasskeysBackend_Change(account.Id, id, Change.Remove<PasskeyCredential>());
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<(AccountFull Account, PasskeyCredential Passkey)> GetOwnPasskey(
        Session session, string id, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);
        var stored = await PasskeysBackend.Get(account.Id, id, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.NotFound<Passkey>();
        return (account, stored);
    }

    private async Task RequireEnabled(CancellationToken cancellationToken)
    {
        if (!await IsEnabled(cancellationToken).ConfigureAwait(false))
            throw StandardError.NotSupported("Passkeys aren't enabled.");
    }

    private async Task CheckRateLimit(string method, Session session, CancellationToken cancellationToken)
    {
        var identities = new RateLimitIdentity[2];
        var identityCount = 0;
        identities[identityCount++] = new RateLimitIdentity(RateLimitIdentityKind.Session, Hash(session.Id));
        if (RateLimitIdentity.ForIP(RpcInboundContext.Current.GetRemoteIPAddress()) is { } ipIdentity)
            identities[identityCount++] = ipIdentity;
        await RateLimitPolicy
            .Check($"{nameof(PasskeyAuth)}.{method}", RateLimitClass.Auth, identities.AsSpan(0, identityCount), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task StoreChallenge(string prefix, Session session, string optionsJson, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        await db.StringSetAsync(prefix + Hash(session.Id), optionsJson, Settings.PasskeyChallengeLifetime).ConfigureAwait(false);
    }

    private async Task<string> ConsumeChallenge(string prefix, Session session, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var value = await db.StringGetDeleteAsync(prefix + Hash(session.Id)).ConfigureAwait(false);
        if (value.IsNullOrEmpty)
            throw StandardError.Constraint("This passkey request has expired. Please try again.");

        return (string)value!;
    }

    private static T Deserialize<T>(string json)
    {
        try {
            return JsonSerializer.Deserialize<T>(json) ?? throw StandardError.Constraint("Invalid passkey response.");
        }
        catch (JsonException) {
            throw StandardError.Constraint("Invalid passkey response.");
        }
    }

    private static string Hash(string value)
        => value.Hash().SHA256().ToBase64HashString(HashAlgorithm.SHA256);
}
```

Check `RateLimitIdentity`'s constructor accepts `(RateLimitIdentityKind, string)` (it does in `PhoneAuth`); check `StandardError.NotFound<T>()` exists (used in `Accounts.OnDeactivateSession`). `Log` comes from `DbServiceBase`. `StringGetDeleteAsync` is GETDEL — Valkey 9 supports it.

- [ ] **Step 6: Settings helpers + `IFido2` registration**

In `src/dotnet/Users.Service/Module/UsersSettings.cs`, add at the end of the class:

```csharp
    public string GetPasskeyRpId(HostInfo hostInfo)
        => PasskeyRpId.IsNullOrEmpty()
            ? hostInfo.BaseUrl.EnsureSuffix("/").ToUri().Host
            : PasskeyRpId;

    public HashSet<string> GetPasskeyOrigins(HostInfo hostInfo)
    {
        var origins = PasskeyOrigins
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();
        if (origins.Count == 0)
            origins.Add(hostInfo.BaseUrl.EnsureSuffix("/").ToUri().GetLeftPart(UriPartial.Authority));
        return origins;
    }
```

In `UsersServiceModule.cs`, next to the `// Passkeys` line from Task 3:

```csharp
        rpcHost.AddApi<IPasskeyAuth, PasskeyAuth>(); // Requires Redis
        services.AddSingleton<IFido2>(c => {
            var settings = c.GetRequiredService<UsersSettings>();
            var hostInfo = c.HostInfo();
            var config = new Fido2Configuration {
                ServerDomain = settings.GetPasskeyRpId(hostInfo),
                ServerName = CoreConstants.AppName,
                Origins = settings.GetPasskeyOrigins(hostInfo),
            };
            return new Fido2(config, null);
        });
```

Add `using Fido2NetLib;` to the module. If `Fido2Configuration.Origins` is init-only or a different set type, assign via the property it exposes (the probe showed `IReadOnlySet<string> Origins`; a `HashSet<string>` satisfies it).

- [ ] **Step 7: Run the tests**

```bash
dotnet test tests/Users.IntegrationTests --filter "FullyQualifiedName~PasskeyAuthTest|FullyQualifiedName~PasskeysBackendTest"
```

Expected: all pass. Typical first-run failures and their meaning:
- `Fido2VerificationException: Invalid origin` → `PasskeyOrigins` in the fixture doesn't match the authenticator's origin string exactly.
- `... rpIdHash` → `PasskeyRpId` vs `SoftwareAuthenticator(rpId)`.
- `SignInShouldRejectNonMonotonicCounter` passing when it shouldn't → Fido2 counter check requires `StoredSignatureCounter` > 0; the first sign-in must have persisted `verified.SignCount`.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/Users.Service tests/Users.IntegrationTests
git commit -m "feat(auth): PasskeyAuth - WebAuthn registration, sign-in, list, rename, delete"
```

---

### Task 6: Shared WebAuthn JSON helpers + web client

**Files:**
- Create: `src/nodejs/src/webauthn-json.ts`
- Create: `tests/ts/unit/webauthn-json.test.ts`
- Modify: `vitest.config.ts`
- Create: `src/dotnet/UI.Blazor/Services/PasskeyUI/passkeys.ts`
- Modify: `src/dotnet/UI.Blazor/exports.ts`
- Create: `src/dotnet/UI.Blazor/Services/PasskeyUI/IPasskeyClient.cs`
- Create: `src/dotnet/UI.Blazor/Services/PasskeyUI/PasskeyCancelledException.cs`
- Create: `src/dotnet/UI.Blazor/Services/PasskeyUI/WebPasskeyClient.cs`
- Modify: `src/dotnet/UI.Blazor/Module/BlazorUICoreModule.cs`

**Interfaces:**
- Produces: `IPasskeyClient { Task<bool> IsAvailable(CancellationToken); Task<string> Create(string optionsJson, CancellationToken); Task<string> Get(string optionsJson, CancellationToken); }`; `PasskeyCancelledException`; JS `Passkeys.isAvailable()`, `Passkeys.create(json)`, `Passkeys.get(json)` returning `{ json?: string; error?: string }` with `error === 'cancelled'` for a dismissed prompt.

- [ ] **Step 1: Write the failing TS test**

Create `tests/ts/unit/webauthn-json.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import {
    base64UrlToBytes,
    bytesToBase64Url,
    creationOptionsFromJson,
    requestOptionsFromJson,
    credentialToJson,
} from 'webauthn-json';

describe('webauthn-json', () => {
    it('round-trips base64url without padding', () => {
        const bytes = new Uint8Array([0, 1, 2, 250, 251, 252, 253, 254, 255]);
        const text = bytesToBase64Url(bytes);
        expect(text).not.toContain('=');
        expect(text).not.toContain('+');
        expect(text).not.toContain('/');
        expect(Array.from(base64UrlToBytes(text))).toEqual(Array.from(bytes));
    });

    it('converts creation options', () => {
        const options = creationOptionsFromJson({
            rp: { id: 'voxt.ai', name: 'Voxt' },
            user: { id: 'AQID', name: 'alice', displayName: 'Alice' },
            challenge: 'BAUG',
            pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
            excludeCredentials: [{ type: 'public-key', id: 'BwgJ' }],
        });
        expect(Array.from(new Uint8Array(options.challenge as ArrayBuffer))).toEqual([4, 5, 6]);
        expect(Array.from(new Uint8Array(options.user.id as ArrayBuffer))).toEqual([1, 2, 3]);
        expect(Array.from(new Uint8Array(options.excludeCredentials![0].id as ArrayBuffer))).toEqual([7, 8, 9]);
        expect(options.user.name).toBe('alice');
    });

    it('converts request options', () => {
        const options = requestOptionsFromJson({
            challenge: 'BAUG',
            rpId: 'voxt.ai',
            allowCredentials: [],
            userVerification: 'required',
        });
        expect(Array.from(new Uint8Array(options.challenge as ArrayBuffer))).toEqual([4, 5, 6]);
        expect(options.rpId).toBe('voxt.ai');
    });

    it('serializes a credential without toJSON support', () => {
        const credential = {
            id: 'AQID',
            rawId: new Uint8Array([1, 2, 3]).buffer,
            type: 'public-key',
            authenticatorAttachment: 'platform',
            response: {
                clientDataJSON: new Uint8Array([4]).buffer,
                authenticatorData: new Uint8Array([5]).buffer,
                signature: new Uint8Array([6]).buffer,
                userHandle: new Uint8Array([7]).buffer,
            },
            getClientExtensionResults: () => ({}),
        } as unknown as PublicKeyCredential;
        const json = credentialToJson(credential) as Record<string, unknown>;
        expect(json.id).toBe('AQID');
        expect(json.rawId).toBe('AQID');
        const response = json.response as Record<string, unknown>;
        expect(response.clientDataJSON).toBe('BA');
        expect(response.signature).toBe('Bg');
        expect(response.userHandle).toBe('Bw');
    });
});
```

- [ ] **Step 2: Add the vitest alias and run to verify failure**

In `vitest.config.ts`, in the `alias` map:

```ts
            'webauthn-json': src('webauthn-json'),
```

```bash
npm run test:unit -- tests/ts/unit/webauthn-json.test.ts
```

Expected: FAIL — module not found.

- [ ] **Step 3: Implement the shared module**

Create `src/nodejs/src/webauthn-json.ts`:

```ts
/**
 * WebAuthn JSON (RFC 9052 base64url fields) ↔ the ArrayBuffer-based browser API. Uses the
 * built-in parse/toJSON when the browser has them and falls back to a manual conversion.
 */

type Json = Record<string, unknown>;

interface PublicKeyCredentialStatic {
    parseCreationOptionsFromJSON?: (json: Json) => PublicKeyCredentialCreationOptions;
    parseRequestOptionsFromJSON?: (json: Json) => PublicKeyCredentialRequestOptions;
}

interface CredentialWithToJson {
    toJSON?: () => Json;
}

export function base64UrlToBytes(text: string): Uint8Array {
    const base64 = text.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
    const binary = atob(padded);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++)
        bytes[i] = binary.charCodeAt(i);

    return bytes;
}

export function bytesToBase64Url(data: ArrayBuffer | Uint8Array): string {
    const bytes = data instanceof Uint8Array ? data : new Uint8Array(data);
    let binary = '';
    for (const byte of bytes)
        binary += String.fromCharCode(byte);

    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function creationOptionsFromJson(json: Json): PublicKeyCredentialCreationOptions {
    const native = publicKeyCredentialStatic()?.parseCreationOptionsFromJSON;
    if (native)
        return native(json);

    const user = json.user as Json;
    return {
        ...json,
        challenge: toBuffer(json.challenge as string),
        user: { ...user, id: toBuffer(user.id as string) } as PublicKeyCredentialUserEntity,
        excludeCredentials: descriptorsFromJson(json.excludeCredentials),
    } as PublicKeyCredentialCreationOptions;
}

export function requestOptionsFromJson(json: Json): PublicKeyCredentialRequestOptions {
    const native = publicKeyCredentialStatic()?.parseRequestOptionsFromJSON;
    if (native)
        return native(json);

    return {
        ...json,
        challenge: toBuffer(json.challenge as string),
        allowCredentials: descriptorsFromJson(json.allowCredentials),
    } as PublicKeyCredentialRequestOptions;
}

export function credentialToJson(credential: PublicKeyCredential): Json {
    const toJson = (credential as unknown as CredentialWithToJson).toJSON;
    if (typeof toJson === 'function')
        return toJson.call(credential);

    const response = credential.response as AuthenticatorAttestationResponse & AuthenticatorAssertionResponse;
    const json: Json = {
        id: credential.id,
        rawId: bytesToBase64Url(credential.rawId),
        type: credential.type,
        authenticatorAttachment: credential.authenticatorAttachment ?? undefined,
        clientExtensionResults: credential.getClientExtensionResults(),
        response: {
            clientDataJSON: bytesToBase64Url(response.clientDataJSON),
        },
    };
    const responseJson = json.response as Json;
    if (response.attestationObject) {
        responseJson.attestationObject = bytesToBase64Url(response.attestationObject);
        if (typeof response.getTransports === 'function')
            responseJson.transports = response.getTransports();
    }
    if (response.authenticatorData)
        responseJson.authenticatorData = bytesToBase64Url(response.authenticatorData);
    if (response.signature)
        responseJson.signature = bytesToBase64Url(response.signature);
    if (response.userHandle)
        responseJson.userHandle = bytesToBase64Url(response.userHandle);

    return json;
}

// Private methods

function publicKeyCredentialStatic(): PublicKeyCredentialStatic | null {
    return typeof PublicKeyCredential === 'undefined'
        ? null
        : PublicKeyCredential as unknown as PublicKeyCredentialStatic;
}

function descriptorsFromJson(value: unknown): PublicKeyCredentialDescriptor[] | undefined {
    if (!Array.isArray(value))
        return undefined;

    return value.map(x => ({ ...(x as Json), id: toBuffer((x as Json).id as string) }) as PublicKeyCredentialDescriptor);
}

function toBuffer(text: string): ArrayBuffer {
    return base64UrlToBytes(text).buffer as ArrayBuffer;
}
```

`base64UrlToBytes` returns a fresh `Uint8Array`, so its `.buffer` is exactly the bytes — no offset slicing needed.

- [ ] **Step 4: Run the TS test**

```bash
npm run test:unit -- tests/ts/unit/webauthn-json.test.ts
```

Expected: 4 passed. (vitest runs under Node where `PublicKeyCredential` is undefined, so the manual paths are what's tested.)

- [ ] **Step 5: The browser bridge**

Create `src/dotnet/UI.Blazor/Services/PasskeyUI/passkeys.ts`:

```ts
import { getLogs } from 'logging';
import { creationOptionsFromJson, requestOptionsFromJson, credentialToJson } from 'webauthn-json';

const { warnLog } = getLogs('Passkeys');

export interface PasskeyResult {
    json?: string;
    error?: string;
}

export class Passkeys {
    public static readonly cancelledError = 'cancelled';

    public static async isAvailable(): Promise<boolean> {
        try {
            if (typeof PublicKeyCredential === 'undefined' || !window.isSecureContext)
                return false;

            return await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
        }
        catch (e) {
            warnLog?.log('isAvailable: failed', e);
            return false;
        }
    }

    public static create(optionsJson: string): Promise<PasskeyResult> {
        return this.run(async () => {
            const publicKey = creationOptionsFromJson(JSON.parse(optionsJson));
            const credential = await navigator.credentials.create({ publicKey });
            return credential as PublicKeyCredential | null;
        });
    }

    public static get(optionsJson: string): Promise<PasskeyResult> {
        return this.run(async () => {
            const publicKey = requestOptionsFromJson(JSON.parse(optionsJson));
            const credential = await navigator.credentials.get({ publicKey });
            return credential as PublicKeyCredential | null;
        });
    }

    // Private methods

    private static async run(ceremony: () => Promise<PublicKeyCredential | null>): Promise<PasskeyResult> {
        try {
            const credential = await ceremony();
            if (!credential)
                return { error: this.cancelledError };

            return { json: JSON.stringify(credentialToJson(credential)) };
        }
        catch (e) {
            const name = (e as { name?: string })?.name;
            if (name === 'NotAllowedError' || name === 'AbortError')
                return { error: this.cancelledError };

            warnLog?.log('run: failed', e);
            return { error: `${name ?? 'Error'}: ${(e as Error)?.message ?? e}` };
        }
    }
}
```

In `src/dotnet/UI.Blazor/exports.ts`, after the `web-auth` export:

```ts
export * from './Services/PasskeyUI/passkeys';
```

- [ ] **Step 6: The C# seam and web implementation**

Create `src/dotnet/UI.Blazor/Services/PasskeyUI/IPasskeyClient.cs`:

```csharp
namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Platform passkey (WebAuthn) ceremonies over standard WebAuthn JSON: the same
/// options/response shapes the server produces and verifies, on every platform.
/// </summary>
public interface IPasskeyClient
{
    Task<bool> IsAvailable(CancellationToken cancellationToken);
    // Throws PasskeyCancelledException when the user dismisses the prompt
    Task<string> Create(string optionsJson, CancellationToken cancellationToken);
    Task<string> Get(string optionsJson, CancellationToken cancellationToken);
}
```

Create `src/dotnet/UI.Blazor/Services/PasskeyUI/PasskeyCancelledException.cs`:

```csharp
namespace ActualChat.UI.Blazor.Services;

public sealed class PasskeyCancelledException() : Exception("The passkey prompt was dismissed.");
```

Create `src/dotnet/UI.Blazor/Services/PasskeyUI/WebPasskeyClient.cs`:

```csharp
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class WebPasskeyClient(UIHub hub) : IPasskeyClient
{
    private const string CancelledError = "cancelled";
    private static readonly string JsClassName = $"{BlazorUICoreModule.ImportName}.Passkeys";

    private IJSRuntime JS { get; } = hub.JS;

    public Task<bool> IsAvailable(CancellationToken cancellationToken)
        => JS.InvokeAsync<bool>($"{JsClassName}.isAvailable", cancellationToken).AsTask();

    public Task<string> Create(string optionsJson, CancellationToken cancellationToken)
        => Run("create", optionsJson, cancellationToken);

    public Task<string> Get(string optionsJson, CancellationToken cancellationToken)
        => Run("get", optionsJson, cancellationToken);

    // Private methods

    private async Task<string> Run(string method, string optionsJson, CancellationToken cancellationToken)
    {
        var result = await JS.InvokeAsync<Result>($"{JsClassName}.{method}", cancellationToken, optionsJson)
            .ConfigureAwait(false);
        if (result.Error == CancelledError)
            throw new PasskeyCancelledException();
        if (!result.Error.IsNullOrEmpty() || result.Json.IsNullOrEmpty())
            throw StandardError.External(result.Error ?? "Passkey ceremony returned nothing.");

        return result.Json;
    }

    // Nested types

    private sealed record Result(string? Json, string? Error);
}
```

Register in `src/dotnet/UI.Blazor/Module/BlazorUICoreModule.cs`, next to `fusion.AddService<TotpUI>(...)`:

```csharp
        services.AddScoped<IPasskeyClient>(c => new WebPasskeyClient(c.UIHub()));
```

MAUI platforms re-register `IPasskeyClient` after this (Tasks 10–11); the last registration wins.

- [ ] **Step 7: Verify the TS build and the C# build**

```bash
npm run build:Verify
dotnet build src/dotnet/UI.Blazor/UI.Blazor.csproj
```

Expected: both succeed, no lint errors.

- [ ] **Step 8: Commit**

```bash
git add src/nodejs/src/webauthn-json.ts tests/ts/unit/webauthn-json.test.ts vitest.config.ts src/dotnet/UI.Blazor
git commit -m "feat(ui): IPasskeyClient with the web (navigator.credentials) implementation"
```

---

### Task 7: `PasskeyUI`, `AccountUI` branch, sign-in modal button, strings

**Files:**
- Create: `src/dotnet/UI.Blazor/Services/PasskeyUI/PasskeyUI.cs`
- Modify: `src/dotnet/UI.Blazor/UIHub.cs`
- Modify: `src/dotnet/UI.Blazor/Module/BlazorUICoreModule.cs`
- Modify: `src/dotnet/UI.Blazor/Services/AccountUI/AccountUI.cs`
- Modify: `src/dotnet/UI.Blazor/Components/SignIn/Modal/ProviderSelectStep.razor`
- Modify: `src/dotnet/UI.Blazor/Components/SignIn/Modal/sign-in-modal.css`
- Modify: `src/dotnet/Localization/Resources/Strings.*.json`, `LocalizedStringsLocalizerExt.cs`

**Interfaces:**
- Produces: `PasskeyUI` with `[ComputeMethod] Task<bool> CanUse(ct)`, `[ComputeMethod] Task<ApiArray<Passkey>> ListOwn(ct)`, `Task<bool> SignIn(ct)`, `Task<Passkey?> Register(ct)`, `Task<bool> Rename(id, name, ct)`, `Task<bool> Delete(id, ct)`; `UIHub.PasskeyUI`.

- [ ] **Step 1: The orchestrator**

Create `src/dotnet/UI.Blazor/Services/PasskeyUI/PasskeyUI.cs`:

```csharp
namespace ActualChat.UI.Blazor.Services;

public class PasskeyUI(UIHub hub) : UIServiceBase<UIHub>(hub), IComputeService
{
    private Task<bool>? _whenClientAvailable;

    private IPasskeyAuth PasskeyAuth => field ??= Services.GetRequiredService<IPasskeyAuth>();
    private IPasskeyClient Client => field ??= Services.GetRequiredService<IPasskeyClient>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    [ComputeMethod]
    public virtual async Task<bool> CanUse(CancellationToken cancellationToken)
    {
        if (!await PasskeyAuth.IsEnabled(cancellationToken).ConfigureAwait(false))
            return false;

        return await IsClientAvailable(cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual Task<ApiArray<Passkey>> ListOwn(CancellationToken cancellationToken)
        => PasskeyAuth.ListOwn(Session, cancellationToken);

    public async Task<bool> SignIn(CancellationToken cancellationToken = default)
    {
        var (optionsJson, beginError) = await UICommander
            .Run(new PasskeyAuth_BeginSignIn { Session = Session }, cancellationToken)
            .ConfigureAwait(false);
        if (beginError != null || optionsJson.IsNullOrEmpty())
            return false;

        string assertionJson;
        try {
            assertionJson = await Client.Get(optionsJson, cancellationToken).ConfigureAwait(false);
        }
        catch (PasskeyCancelledException) {
            return false;
        }

        var (_, error) = await UICommander
            .Run(new PasskeyAuth_CompleteSignIn { Session = Session, AssertionJson = assertionJson }, cancellationToken)
            .ConfigureAwait(false);
        return error == null;
    }

    public async Task<Passkey?> Register(CancellationToken cancellationToken = default)
    {
        var (optionsJson, beginError) = await UICommander
            .Run(new PasskeyAuth_BeginRegistration { Session = Session }, cancellationToken)
            .ConfigureAwait(false);
        if (beginError != null || optionsJson.IsNullOrEmpty())
            return null;

        string attestationJson;
        try {
            attestationJson = await Client.Create(optionsJson, cancellationToken).ConfigureAwait(false);
        }
        catch (PasskeyCancelledException) {
            return null;
        }

        var command = new PasskeyAuth_CompleteRegistration { Session = Session, AttestationJson = attestationJson };
        var (passkey, error) = await UICommander.Run(command, cancellationToken).ConfigureAwait(false);
        return error == null ? passkey : null;
    }

    public async Task<bool> Rename(string id, string name, CancellationToken cancellationToken = default)
    {
        var command = new PasskeyAuth_Rename { Session = Session, Id = id, Name = name };
        var (_, error) = await UICommander.Run(command, cancellationToken).ConfigureAwait(false);
        return error == null;
    }

    public async Task<bool> Delete(string id, CancellationToken cancellationToken = default)
    {
        var command = new PasskeyAuth_Delete { Session = Session, Id = id };
        var (_, error) = await UICommander.Run(command, cancellationToken).ConfigureAwait(false);
        return error == null;
    }

    // Private methods

    private Task<bool> IsClientAvailable(CancellationToken cancellationToken)
    {
        // Cached per scope: the answer is a property of the device, not of the moment
        var whenAvailable = _whenClientAvailable;
        if (whenAvailable is { IsCompletedSuccessfully: true })
            return whenAvailable;

        return _whenClientAvailable = Probe();

        async Task<bool> Probe() {
            try {
                return await Client.IsAvailable(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                Log.LogWarning(e, "IsClientAvailable: probe failed");
                return false;
            }
        }
    }
}
```

`UICommander.Run` returns a tuple `(TResult Value, Exception? Error)` — same use as `TotpUI.SendCode`. On Blazor Server the JS probe can run before the circuit is interactive; that's why a failed probe isn't cached (only a successful task is reused).

- [ ] **Step 2: Wire it**

In `src/dotnet/UI.Blazor/UIHub.cs`, after `TotpUI`:

```csharp
    public PasskeyUI PasskeyUI => field ??= Services.GetRequiredService<PasskeyUI>();
```

In `BlazorUICoreModule.cs`, after `fusion.AddService<TotpUI>(ServiceLifetime.Scoped);`:

```csharp
        fusion.AddService<PasskeyUI>(ServiceLifetime.Scoped);
```

In `src/dotnet/UI.Blazor/Services/AccountUI/AccountUI.cs`, change `SignIn`:

```csharp
    public async Task SignIn(string schema)
    {
        if (schema == AuthSchema.Passkey)
            await Hub.PasskeyUI.SignIn().ConfigureAwait(false);
        else
            await SignInBackend(schema).ConfigureAwait(false);
        // TODO(AY): Make it reliable
        await NotificationUI.EnsureDeviceRegistered(CancellationToken.None).ConfigureAwait(false);
    }
```

- [ ] **Step 3: Strings**

Add to `src/dotnet/Localization/Resources/Strings.en.json` (a new `Passkeys_` group with a context comment in the file's style, plus one `SignIn_` key next to the other sign-in keys):

| Key | English |
|---|---|
| `SignIn_SignInWithPasskey` | `Sign in with a passkey` |
| `Settings_Passkeys` | `Passkeys` |
| `Passkeys_Intro` | `Sign in with Face ID, Touch ID, fingerprint or Windows Hello instead of a code.` |
| `Passkeys_Add` | `Add a passkey` |
| `Passkeys_Unavailable` | `Passkeys aren't available on this device or browser.` |
| `Passkeys_YourPasskeys` | `Your passkeys` |
| `Passkeys_NotSetUp` | `Not set up` |
| `Passkeys_Count` | `{0} passkey\|{0} passkeys` (plural) |
| `Passkeys_Synced` | `Synced` |
| `Passkeys_ThisDeviceOnly` | `This device only` |
| `Passkeys_Added_Format` | `Added {0}` |
| `Passkeys_LastUsed_Format` | `Last used {0}` |
| `Passkeys_NeverUsed` | `Never used` |
| `Passkeys_Rename` | `Rename` |
| `Passkeys_RenameTitle` | `Rename passkey` |
| `Passkeys_Name` | `Name` |
| `Passkeys_Delete` | `Delete` |
| `Passkeys_DeleteConfirm_Format` | `Delete "{0}"? You won't be able to sign in with it anymore.` |
| `Passkeys_Added` | `Passkey added` |
| `Onboarding_PasskeyTitle` | `Sign in faster next time` |
| `Onboarding_PasskeyText` | `Create a passkey and use Face ID, Touch ID, fingerprint or Windows Hello instead of waiting for a code.` |
| `Onboarding_CreatePasskey` | `Create passkey` |
| `Onboarding_NotNow` | `Not now` |

Add each key, translated, to **every** other hand-written `Strings.*.json` (all 22 languages; the plural key gets the language's number of forms — see `Sessions_DaysAgo` in each file for how many). Add the typed members to `LocalizedStringsLocalizerExt.cs` in the same style as their neighbours (`Passkeys_Count(long count, object arg0) => l.Plural(...)`, `_Format(object arg0)` members, plain `=> l["..."].Value` for the rest). Then:

```bash
scripts/derive-bcms.cmd
scripts/derive-max.cmd
dotnet test tests/Core.UnitTests --filter "FullyQualifiedName~AppLocalizationTest"
```

(Find where `AppLocalizationTest` lives with `grep -rl "class AppLocalizationTest" tests` and use that project.) Expected: green.

- [ ] **Step 4: The sign-in modal button**

In `ProviderSelectStep.razor`, extend `Model` and `ComputeState`:

```csharp
    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var hasSentCodeRecently = await TotpUI.HasSentCodeRecently(CancellationToken.None);
        var signInError = await SessionTemporals.Get(Session, SessionTemporals_SignInErrorKey, cancellationToken)
            ?? "";
        var canUsePasskey = await PasskeyUI.CanUse(cancellationToken);
        return new(hasSentCodeRecently, signInError, canUsePasskey);
    }
```

Find the `Model` record in the file (it is `public sealed record Model(bool HasSentCodeRecently = false, string SignInError = "")` or similar) and add a third `bool CanUsePasskey = false` member. Add `private PasskeyUI PasskeyUI => Hub.PasskeyUI;` next to `TotpUI`.

In the markup, at the top of `<div class="c-auth-providers">`, before the `@foreach`:

```razor
        @if (m.CanUsePasskey) {
            <Button
                Class="btn-modal sign-in sign-in-with sign-in-passkey"
                Click="@(_ => OnSignInWithProvider(AuthSchema.Passkey))">
                <Icon><i class="icon-key"></i></Icon>
                <Title>@L.SignIn_SignInWithPasskey</Title>
            </Button>
        }
```

In `sign-in-modal.css`, next to the existing `.sign-in-with` rules, size the icon like the provider images (read the neighbouring rule for the exact selector and dimensions):

```css
.sign-in-modal .sign-in-passkey .icon-key {
    @apply text-2xl;
}
```

(If the project's CSS doesn't use Tailwind `@apply` in this file, write `font-size: 1.5rem;` instead — match the file.)

- [ ] **Step 5: Build, verify manually**

```bash
dotnet build src/dotnet/UI.Blazor/UI.Blazor.csproj
```

Manual check (server running via the host's `run-watch.cmd`, or `/server-start`): open the app signed out in Chrome, DevTools → More tools → WebAuthn → Enable → Add virtual authenticator (ctap2, internal, resident key + user verification). The sign-in modal shows "Sign in with a passkey". Sign in with phone/email first, then in a later task register a passkey; for now confirm the button appears and, with no passkey registered, clicking it shows the "isn't linked to an account" error toast after the virtual authenticator prompt.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor src/dotnet/Localization
git commit -m "feat(ui): PasskeyUI and 'Sign in with a passkey' in the sign-in modal"
```

---

### Task 8: Settings — Passkeys tab, Account row, badge

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/Settings/SettingsTabId.cs`
- Create: `src/dotnet/UI.Blazor.App/Components/Settings/PasskeySettings.razor`
- Create: `src/dotnet/UI.Blazor.App/Components/Settings/PasskeyRenameModal.razor`
- Create: `src/dotnet/UI.Blazor.App/Components/Settings/passkey-settings.css`
- Modify: `src/dotnet/UI.Blazor.App/Components/Settings/SettingsModal.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/Settings/YourAccount.razor`
- Modify: `src/dotnet/UI.Blazor/Components/SettingsPanel/SettingsPanel.razor`
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs` (modal view registration — find the `AddTypeMapper<IModalView>` block)

Read `docs/ui/components.md` first (file layout, `c-` class prefix, where child styles go).

- [ ] **Step 1: Tab id and `SelectTab`**

In `SettingsTabId.cs`, after `Account`:

```csharp
    public static readonly string Passkeys = nameof(Passkeys).Decapitalize();
```

In `SettingsPanel.razor`, add a public method next to `OnTabArrowClick`:

```csharp
    public void SelectTab(string tabId) {
        var tab = Tabs.FirstOrDefault(x => x.Id == tabId);
        if (tab is null || tab.Content is null)
            return;

        SelectedTabId = tab.Id;
        _isRightSideVisible = true;
        StepIn();
        StateHasChanged();
    }
```

- [ ] **Step 2: The tab page**

Create `PasskeySettings.razor`:

```razor
@namespace ActualChat.UI.Blazor.App.Components
@inherits ComputedStateComponent<AppUIHub, PasskeySettings.Model>
@{
    var m = State.Value;
    var passkeys = m.Passkeys;
}

<TileTopic Topic="@L.Settings_Passkeys"/>

<Tile Class="passkey-intro-tile">
    <TileItem IsHoverable="false">
        <Content>
            <span class="c-intro">@L.Passkeys_Intro</span>
        </Content>
    </TileItem>
</Tile>

<ButtonTile Class="btn-primary" Filled="true" IsDisabled="@(!m.CanUse)" Click="@OnAddClick">
    <Content>@L.Passkeys_Add</Content>
</ButtonTile>
@if (!m.CanUse) {
    <div class="passkey-unavailable">@L.Passkeys_Unavailable</div>
}

@if (passkeys.Count > 0) {
    <TileTopic Topic="@L.Passkeys_YourPasskeys"/>

    <Tile Class="passkey-tile">
        @foreach (var passkey in passkeys) {
            <TileItem IsHoverable="false">
                <Icon>
                    <i class="icon-key"></i>
                </Icon>
                <Content>
                    <span class="c-name">@passkey.Name</span>
                    <span class="c-pill @(passkey.IsSynced ? "synced" : "")">
                        @(passkey.IsSynced ? L.Passkeys_Synced : L.Passkeys_ThisDeviceOnly)
                    </span>
                </Content>
                <Caption>
                    <span>@L.Passkeys_Added_Format(FormatDate(passkey.CreatedAt))</span>
                    <span> · </span>
                    <span>
                        @(passkey.LastUsedAt is { } lastUsedAt
                            ? L.Passkeys_LastUsed_Format(FormatDate(lastUsedAt))
                            : L.Passkeys_NeverUsed)
                    </span>
                </Caption>
                <Right>
                    <div class="c-actions">
                        <ButtonRound
                            Class="btn-sm btn-transparent"
                            Tooltip="@L.Passkeys_Rename"
                            Click="@(() => OnRenameClick(passkey))">
                            <i class="icon-edit text-xl"></i>
                        </ButtonRound>
                        <ButtonRound
                            Class="btn-sm btn-danger"
                            Tooltip="@L.Passkeys_Delete"
                            Click="@(() => OnDeleteClick(passkey))">
                            <i class="icon-trash03 text-xl"></i>
                        </ButtonRound>
                    </div>
                </Right>
            </TileItem>
            @if (passkey != passkeys[^1]) {
                <Divider />
            }
        }
    </Tile>
}

@code {
    private PasskeyUI PasskeyUI => Hub.PasskeyUI;

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() {
            InitialValue = new Model(),
            Category = GetStateCategory(GetType()),
        };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var canUse = await PasskeyUI.CanUse(cancellationToken);
        var passkeys = await PasskeyUI.ListOwn(cancellationToken);
        return new Model {
            CanUse = canUse,
            Passkeys = passkeys,
        };
    }

    private string FormatDate(Moment moment)
        => DateTimeConverter.ToLocalTime(moment.ToDateTime()).ToString("d", DateFormatter);

    private async Task OnAddClick() {
        var passkey = await PasskeyUI.Register();
        if (passkey != null)
            ToastUI.Show(L.Passkeys_Added, "icon-checkmark-circle", ToastDismissDelay.Short);
    }

    private Task OnRenameClick(Passkey passkey)
        => ModalUI.Show(new PasskeyRenameModal.Model(passkey));

    private async Task OnDeleteClick(Passkey passkey) {
        var isConfirmed = await ModalUI.Confirm(L.Passkeys_DeleteConfirm_Format(passkey.Name), L.Passkeys_Delete);
        if (!isConfirmed)
            return;

        await PasskeyUI.Delete(passkey.Id);
    }

    // Nested types

    public sealed record Model {
        public bool CanUse { get; init; }
        public ApiArray<Passkey> Passkeys { get; init; } = ApiArray<Passkey>.Empty;
    }
}
```

`ModalUI.Confirm(...)` — check how `SessionSettings.razor`'s sign-out-all confirmation is done (`ConfirmModal.Model` via `ModalUI.Show`, or a `Confirm` helper) and use the same call; `ToastUI`/`ModalUI`/`DateTimeConverter`/`DateFormatter` are on the component base already (see `ApiKeySettings.razor`, `SettingsModal.razor`).

Create `PasskeyRenameModal.razor`, modelled on `OwnAccountEditorModal` (one `TextBox` bound to `_name`, Save calls `PasskeyUI.Rename(Model.Passkey.Id, _name)` and closes on success; title `@L.Passkeys_RenameTitle`, label `@L.Passkeys_Name`). Register `.Add<PasskeyRenameModal.Model, PasskeyRenameModal>()` in the app module's `IModalView` type mapper (grep `OwnAccountEditorModal.Model` to find the block).

Create `passkey-settings.css`:

```css
.passkey-tile .c-name {
    @apply mr-2;
}
.passkey-tile .c-pill {
    @apply px-2 py-0.5 rounded-full text-caption-6 bg-03 text-03;
}
.passkey-tile .c-pill.synced {
    @apply bg-success-bg text-success;
}
.passkey-tile .c-actions {
    @apply flex-x gap-x-1;
}
.passkey-unavailable {
    @apply text-caption-6 text-03 px-4 pb-2;
}
```

Use the colour/spacing utilities that already exist in the project's Tailwind config (grep `api-key-tile` in `settings-modal.css` or the `ApiKeySettings` styles and reuse those tokens). Register the CSS the way `settings-modal.css` is (check `docs/ui/components.md` — the bundler picks up `*.css` next to components).

- [ ] **Step 3: The tab, the row, the badge**

In `SettingsModal.razor`:
- `ComputeState`: add

```csharp
        var needsPasskey = await Hub.PasskeyUI.CanUse(cancellationToken).ConfigureAwait(false)
            && (await Hub.PasskeyUI.ListOwn(cancellationToken).ConfigureAwait(false)).Count == 0;
```

  and `NeedsPasskey = needsPasskey` in the returned model; add `public bool NeedsPasskey { get; init; }` to `ComputedModel`.
- Add a `private SettingsPanel _panel = null!;` field and `@ref="_panel"` on the `<SettingsPanel .../>` element.
- Tabs: change the Account tab to

```csharp
        tabs.Add(new(SettingsTabId.Account, L.Settings_YourAccount) {
            IconClass = "icon-person",
            TitleBadge = m.NeedsPasskey ? @<WarningBadge IsMuted="true"/> : null,
            Content = @<YourAccount OpenPasskeys="@(() => _panel.SelectTab(SettingsTabId.Passkeys))"/>,
        });
        tabs.Add(new(SettingsTabId.Passkeys, L.Settings_Passkeys) {
            IconClass = "icon-key",
            TitleBadge = m.NeedsPasskey ? @<WarningBadge IsMuted="true"/> : null,
            Content = @<PasskeySettings/>,
        });
```

  (both inside `if (showMyAccount)`).

In `YourAccount.razor`, add `[Parameter] public Action? OpenPasskeys { get; set; }` and, after the phone `TileItem`:

```razor
    @if (_showPasskeys) {
        <TileItem Click="@(() => OpenPasskeys?.Invoke())">
            <Icon>
                <i class="icon-key"></i>
            </Icon>
            <Content>
                @(_passkeyCount > 0 ? L.Passkeys_Count(_passkeyCount, _passkeyCount) : L.Passkeys_NotSetUp)
            </Content>
            <Caption>
                @L.Settings_Passkeys
            </Caption>
            <Right>
                @if (_passkeyCount == 0) {
                    <WarningBadge IsMuted="true"/>
                }
                <i class="icon-chevron-right text-xl text-03"></i>
            </Right>
        </TileItem>
    }
```

with, in `ComputeState`:

```csharp
        var canUsePasskey = await Hub.PasskeyUI.CanUse(cancellationToken);
        var passkeys = await Hub.PasskeyUI.ListOwn(cancellationToken);
        _passkeyCount = passkeys.Count;
        _showPasskeys = canUsePasskey || _passkeyCount > 0;
```

and the two fields `private int _passkeyCount; private bool _showPasskeys;`. (If `icon-chevron-right` doesn't exist, grep `icon-chevron` in `src/dotnet/UI.Blazor/**/*.razor` for the name in use.)

- [ ] **Step 4: Build, then verify manually**

```bash
dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj
```

Manually, with the Chrome virtual authenticator from Task 7: Settings → Account shows the Passkeys row with "Not set up" + badge; clicking it opens the Passkeys tab; "Add a passkey" → virtual authenticator → the list shows one passkey named "Passkey" with "Synced"; rename works; sign out → "Sign in with a passkey" signs back in; delete works and the badge returns.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor src/dotnet/UI.Blazor.App
git commit -m "feat(ui): Passkeys settings tab, Account row and badge"
```

---

### Task 9: Post-sign-in nudge as an onboarding step

**Files:**
- Modify: `src/dotnet/Api/LocalOnboardingSettings.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/OnboardingUI/OnboardingUI.cs`
- Create: `src/dotnet/UI.Blazor.App/Components/Onboarding/PasskeyStep.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/Onboarding/OnboardingModal.razor`

- [ ] **Step 1: Snooze state**

In `src/dotnet/Api/LocalOnboardingSettings.cs`, after `AreCookiesAccepted` (this type is inside the legacy MemoryPack closure, so it gets both numberings — see `docs/architecture/serialization.md`, "Adding a member to a type already inside the closure"):

```csharp
    [DataMember, MemoryPackOrder(2), Key(2)] public int PasskeyNudgeCount { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public Moment PasskeyNudgeLastAt { get; init; }
```

- [ ] **Step 2: Decide when to nudge**

In `OnboardingUI.cs`, add:

```csharp
    private const int MaxPasskeyNudgeCount = 3;
    private static readonly TimeSpan PasskeyNudgeInterval = TimeSpan.FromDays(7);

    private PasskeyUI PasskeyUI => Hub.PasskeyUI;

    // Not a compute method: OnboardingUI is a plain scoped service (services.AddScoped in
    // BlazorUIAppModule), and the answer is only needed at the moment the modal opens
    public async Task<bool> ShouldShowPasskeyStep(CancellationToken cancellationToken)
    {
        if (!await PasskeyUI.CanUse(cancellationToken).ConfigureAwait(false))
            return false;

        var passkeys = await PasskeyUI.ListOwn(cancellationToken).ConfigureAwait(false);
        if (passkeys.Count > 0)
            return false;

        await LocalSettings.WhenRead.ConfigureAwait(false);
        var local = LocalSettings.Value;
        if (local.PasskeyNudgeCount >= MaxPasskeyNudgeCount)
            return false;

        return local.PasskeyNudgeLastAt == default
            || Clocks.SystemClock.Now - local.PasskeyNudgeLastAt > PasskeyNudgeInterval;
    }

    public void SnoozePasskeyStep()
    {
        var local = LocalSettings.Value;
        UpdateLocalSettings(local with {
            PasskeyNudgeCount = local.PasskeyNudgeCount + 1,
            PasskeyNudgeLastAt = Clocks.SystemClock.Now,
        });
    }
```

In `ShouldBeShown`, after the `UserSettings.Value.HasUncompletedSteps()` check:

```csharp
        if (await ShouldShowPasskeyStep(cancellationToken).ConfigureAwait(false))
            return true;
```

- [ ] **Step 3: The step**

Create `PasskeyStep.razor`:

```razor
@namespace ActualChat.UI.Blazor.App.Components
@inherits SimpleStep<AppUIHub>;

@if (CurrentStep != this) {
    return;
}

<div class="passkey-step">
    <p class="step-title">@L.Onboarding_PasskeyTitle</p>
    <p class="step-subtitle">@L.Onboarding_PasskeyText</p>
</div>

@code {
    private OnboardingUI OnboardingUI => Hub.OnboardingUI;
    private PasskeyUI PasskeyUI => Hub.PasskeyUI;

    [Parameter, EditorRequired] public bool IsRequired { get; set; }

    public override bool CanSkip => true;
    public override string SkipTitle => L.Onboarding_NotNow;
    public override string NextTitle => L.Onboarding_CreatePasskey;
    public override bool IsCompleted => !IsRequired;

    protected override Task<bool> Validate()
        => Task.FromResult(true);

    protected override async Task<bool> Save() {
        var passkey = await PasskeyUI.Register();
        return passkey != null;
    }

    protected override ValueTask OnSkip() {
        OnboardingUI.SnoozePasskeyStep();
        return ValueTask.CompletedTask;
    }
}
```

In `OnboardingModal.razor`:
- `ViewModel`: add `public bool ShowPasskeyStep { get; init; }`; in `ComputeState` set `ShowPasskeyStep = await OnboardingUI.ShouldShowPasskeyStep(cancellationToken).ConfigureAwait(false)` (add `private OnboardingUI OnboardingUI => Hub.OnboardingUI;`).
- `<Steps>`: add `<PasskeyStep IsRequired="m.ShowPasskeyStep"/>` after `<DataCollectionStep .../>`.
- Footer: the button shows `stepper.NextTitle` for `PasskeyStep`, so add `or PasskeyStep` to the `@if (stepper.CurrentStep is PhoneStep or EmailStep or DataCollectionStep)` condition.

- [ ] **Step 4: Build and verify**

```bash
dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj
```

Manually: sign out; delete the passkey if one exists; sign in with a code. The onboarding modal opens on the passkey step ("Sign in faster next time"); "Not now" closes it and it doesn't reappear on the next sign-in (7-day snooze); clearing site data resets it; "Create passkey" runs the virtual authenticator and the step completes.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api/LocalOnboardingSettings.cs src/dotnet/UI.Blazor.App
git commit -m "feat(ui): passkey nudge after sign-in as a snoozable onboarding step"
```

---

### Task 10: Android — Credential Manager client, asset links, apk-key-hash origins

**Files:**
- Modify: `Directory.Packages.props`, `src/dotnet/App.Maui/App.Maui.csproj`
- Create: `src/dotnet/App.Maui/Platforms/Android/AndroidPasskeyClient.cs`
- Modify: `src/dotnet/App.Maui/MauiProgram.Android.cs`
- Modify: `src/dotnet/App.Wasm/wwwroot/.well-known/assetlinks.json`
- Modify: `src/dotnet/App.Server/appsettings.json` (or wherever `UsersSettings` prod/dev values live — see Step 5)

- [ ] **Step 1: Packages**

```bash
dotnet package search Xamarin.AndroidX.Credentials --take 5
dotnet package search Xamarin.AndroidX.Credentials.PlayServicesAuth --take 5
```

Pin the latest stable of each in `Directory.Packages.props`:

```xml
    <PackageVersion Include="Xamarin.AndroidX.Credentials" Version="<latest stable>" />
    <PackageVersion Include="Xamarin.AndroidX.Credentials.PlayServicesAuth" Version="<latest stable>" />
```

and reference both in `App.Maui.csproj` inside the Android-only `ItemGroup` (the one with `Xamarin.GooglePlayServices.Auth`):

```xml
    <PackageReference Include="Xamarin.AndroidX.Credentials" />
    <PackageReference Include="Xamarin.AndroidX.Credentials.PlayServicesAuth" />
```

- [ ] **Step 2: The client**

Create `src/dotnet/App.Maui/Platforms/Android/AndroidPasskeyClient.cs`:

```csharp
using Android.Gms.Common;
using Android.OS;
using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;
using ActualChat.UI.Blazor.Services;
using Java.Util.Concurrent;

namespace ActualChat.App.Maui;

public sealed class AndroidPasskeyClient(IServiceProvider services) : IPasskeyClient
{
    private ICredentialManager Manager { get; } = CredentialManager.Create(Platform.AppContext);
    private IExecutorService Executor { get; } = services.GetRequiredService<IExecutorService>();

    public Task<bool> IsAvailable(CancellationToken cancellationToken)
    {
        if (MauiSettings.IsHostOverridden)
            return Task.FromResult(false);

        var status = GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(Platform.AppContext);
        return Task.FromResult(status == ConnectionResult.Success);
    }

    public async Task<string> Create(string optionsJson, CancellationToken cancellationToken)
    {
        var request = new CreatePublicKeyCredentialRequest(optionsJson);
        var callback = new Callback<CreateCredentialResponse>();
        using var signal = new CancellationSignal();
        using var _ = cancellationToken.Register(signal.Cancel);
        Manager.CreateCredentialAsync(MainActivity.Current, request, signal, Executor, callback);
        var response = await callback.WhenCompleted.ConfigureAwait(false);
        return ((CreatePublicKeyCredentialResponse)response).RegistrationResponseJson;
    }

    public async Task<string> Get(string optionsJson, CancellationToken cancellationToken)
    {
        var request = new GetCredentialRequest.Builder()
            .AddCredentialOption(new GetPublicKeyCredentialOption(optionsJson))
            .Build();
        var callback = new Callback<GetCredentialResponse>();
        using var signal = new CancellationSignal();
        using var _ = cancellationToken.Register(signal.Cancel);
        Manager.GetCredentialAsync(MainActivity.Current, request, signal, Executor, callback);
        var response = await callback.WhenCompleted.ConfigureAwait(false);
        return ((PublicKeyCredential)response.Credential).AuthenticationResponseJson;
    }

    // Nested types

    private sealed class Callback<TResponse> : Java.Lang.Object, ICredentialManagerCallback
        where TResponse : Java.Lang.Object
    {
        private readonly TaskCompletionSource<TResponse> _source = TaskCompletionSourceExt.New<TResponse>();

        public Task<TResponse> WhenCompleted => _source.Task;

        public void OnResult(Java.Lang.Object? result)
            => _source.TrySetResult((TResponse)result!);

        public void OnError(Java.Lang.Object? error)
        {
            var exception = error switch {
                GetCredentialCancellationException or CreateCredentialCancellationException or NoCredentialException
                    => new PasskeyCancelledException(),
                Java.Lang.Throwable throwable => StandardError.External(throwable.Message ?? throwable.ToString()),
                _ => StandardError.External("Passkey ceremony failed."),
            };
            _source.TrySetException(exception);
        }
    }
}
```

The binding's exact names (`ICredentialManagerCallback`, the `*CancellationException` types, `CreateCredentialAsync` vs `CreateCredential` overload with `CancellationSignal`) come from the NuGet you pinned: build once and fix names against the compiler's suggestions / the decompiled binding in the IDE. `IExecutorService` is already registered in `MauiProgram.Android.cs` (line ~29).

- [ ] **Step 3: Register**

In `MauiProgram.Android.cs`, after `services.AddSingleton(c => new NativeGoogleAuth(c));`:

```csharp
        services.AddScoped<IPasskeyClient>(c => new AndroidPasskeyClient(c));
```

- [ ] **Step 4: Asset links**

In `src/dotnet/App.Wasm/wwwroot/.well-known/assetlinks.json`, change both `relation` arrays to:

```json
    "relation": [
      "delegate_permission/common.handle_all_urls",
      "delegate_permission/common.get_login_creds"
    ],
```

- [ ] **Step 5: Server origins for the app**

Compute the apk-key-hash for each signing cert fingerprint in `assetlinks.json` (hex SHA-256 → base64url):

```bash
echo "7F:34:78:A4:EA:25:B1:F9:81:4B:F6:A8:7D:3E:CF:72:C5:CC:01:CF:C8:8A:19:1D:EB:E3:BA:B5:66:C3:7D:0B" | tr -d ':' | xxd -r -p | basenc --base64url | tr -d '='
echo "A1:45:D6:FC:25:B8:3B:A0:58:0D:7B:7D:9E:AF:42:1E:A4:D3:A4:D9:3F:AC:B6:0F:5E:30:E2:C8:89:00:5E:77" | tr -d ':' | xxd -r -p | basenc --base64url | tr -d '='
```

Then set `UsersSettings:PasskeyOrigins` for dev and prod to `https://<host>;android:apk-key-hash:<dev hash>;android:apk-key-hash:<prod hash>`. Where: grep `UsersSettings` in `src/dotnet/App.Server/appsettings*.json` and the deployment config (`k8s/` or `deploy/` — `grep -rl "UsersSettings__" --include=*.yaml --include=*.yml --include=*.json .`) and add the value alongside the other `UsersSettings` entries for each environment. If deployment config isn't in this repo, put the dev value in `appsettings.Development.json` and list the prod value in the PR description for ops.

- [ ] **Step 6: Build**

```bash
dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net11.0-android
```

(See memory `build-android-locally-ios-via-mba-ssh`: the Android workload is installed in WSL.) Expected: success. Device pass — install a dev build (see memory `wsl-adb-no-usb-passthrough` for the adb recipe), sign in, Settings → Passkeys → Add → Google Password Manager sheet → passkey listed as "Google Password Manager"; sign out; "Sign in with a passkey" → fingerprint → signed in. Note the result in the PR description.

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props src/dotnet/App.Maui src/dotnet/App.Wasm/wwwroot/.well-known/assetlinks.json src/dotnet/App.Server
git commit -m "feat(android): passkeys via Credential Manager"
```

---

### Task 11: Apple — `ASAuthorizationController` client, entitlements, AASA

**Files:**
- Create: `src/dotnet/App.Maui/MaciOS/Services/ApplePasskeyJson.cs`
- Create: `src/dotnet/App.Maui/MaciOS/Services/ApplePasskeyClient.cs`
- Test: `tests/Users.IntegrationTests/ApplePasskeyJsonTest.cs` is not possible (MAUI-only project) — the JSON helper is pure C#, so put it in a shared spot instead: create it in `src/dotnet/UI.Blazor/Services/PasskeyUI/PasskeyJson.cs` and test it from `tests/UI.Blazor.IntegrationTests/PasskeyJsonTest.cs`.
- Modify: `src/dotnet/App.Maui/MauiProgram.iOS.cs`, `MauiProgram.MacCatalyst.cs`, `MauiProgram.MacOS.cs`
- Modify: `src/dotnet/App.Maui/Platforms/{iOS,MacCatalyst,MacOS}/Entitlements.{dev,prod}.plist`
- Modify: `src/dotnet/App.Wasm/wwwroot/.well-known/apple-app-site-association.json`

- [ ] **Step 1: Failing test for the JSON assembly**

Create `tests/UI.Blazor.IntegrationTests/PasskeyJsonTest.cs` (use whatever plain test base the project's other unit-style tests use):

```csharp
using System.Text.Json;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.IntegrationTests;

public class PasskeyJsonTest
{
    [Fact]
    public void ParseCreationOptionsShouldExtractRpUserAndChallenge()
    {
        // arrange
        const string json = """
            {"rp":{"id":"voxt.ai","name":"Voxt"},"user":{"id":"AQID","name":"alice","displayName":"Alice"},
             "challenge":"BAUG","pubKeyCredParams":[{"type":"public-key","alg":-7}]}
            """;

        // act
        var options = PasskeyJson.ParseCreationOptions(json);

        // assert
        options.RpId.Should().Be("voxt.ai");
        options.UserName.Should().Be("alice");
        options.UserId.Should().Equal([1, 2, 3]);
        options.Challenge.Should().Equal([4, 5, 6]);
    }

    [Fact]
    public void ParseRequestOptionsShouldExtractRpAndChallenge()
    {
        // act
        var options = PasskeyJson.ParseRequestOptions("""{"challenge":"BAUG","rpId":"voxt.ai","allowCredentials":[]}""");

        // assert
        options.RpId.Should().Be("voxt.ai");
        options.Challenge.Should().Equal([4, 5, 6]);
    }

    [Fact]
    public void AttestationJsonShouldMatchWebAuthnShape()
    {
        // act
        var json = PasskeyJson.Attestation(
            credentialId: [1, 2, 3], clientDataJson: [4], attestationObject: [5]);

        // assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("id").GetString().Should().Be("AQID");
        root.GetProperty("rawId").GetString().Should().Be("AQID");
        root.GetProperty("type").GetString().Should().Be("public-key");
        root.GetProperty("response").GetProperty("clientDataJSON").GetString().Should().Be("BA");
        root.GetProperty("response").GetProperty("attestationObject").GetString().Should().Be("BQ");
        root.GetProperty("response").GetProperty("transports")[0].GetString().Should().Be("internal");
    }

    [Fact]
    public void AssertionJsonShouldMatchWebAuthnShape()
    {
        // act
        var json = PasskeyJson.Assertion(
            credentialId: [1, 2, 3], clientDataJson: [4], authenticatorData: [5], signature: [6], userHandle: [7]);

        // assert
        using var doc = JsonDocument.Parse(json);
        var response = doc.RootElement.GetProperty("response");
        response.GetProperty("authenticatorData").GetString().Should().Be("BQ");
        response.GetProperty("signature").GetString().Should().Be("Bg");
        response.GetProperty("userHandle").GetString().Should().Be("Bw");
    }
}
```

Run: `dotnet test tests/UI.Blazor.IntegrationTests --filter "FullyQualifiedName~PasskeyJsonTest"` → FAIL (type missing).

- [ ] **Step 2: The helper**

Create `src/dotnet/UI.Blazor/Services/PasskeyUI/PasskeyJson.cs`:

```csharp
using System.Buffers.Text;
using System.Text.Json;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// WebAuthn JSON for native clients that hand back raw blobs (Apple) instead of a serialized credential.
/// </summary>
public static class PasskeyJson
{
    public sealed record CreationOptions(string RpId, string UserName, byte[] UserId, byte[] Challenge);
    public sealed record RequestOptions(string RpId, byte[] Challenge);

    public static CreationOptions ParseCreationOptions(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var user = root.GetProperty("user");
        return new CreationOptions(
            root.GetProperty("rp").GetProperty("id").GetString()!,
            user.GetProperty("name").GetString()!,
            Base64Url.DecodeFromChars(user.GetProperty("id").GetString()!),
            Base64Url.DecodeFromChars(root.GetProperty("challenge").GetString()!));
    }

    public static RequestOptions ParseRequestOptions(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new RequestOptions(
            root.GetProperty("rpId").GetString()!,
            Base64Url.DecodeFromChars(root.GetProperty("challenge").GetString()!));
    }

    public static string Attestation(byte[] credentialId, byte[] clientDataJson, byte[] attestationObject)
    {
        var id = Base64Url.EncodeToString(credentialId);
        return JsonSerializer.Serialize(new {
            id,
            rawId = id,
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new {
                clientDataJSON = Base64Url.EncodeToString(clientDataJson),
                attestationObject = Base64Url.EncodeToString(attestationObject),
                transports = new[] { "internal" },
            },
        });
    }

    public static string Assertion(
        byte[] credentialId, byte[] clientDataJson, byte[] authenticatorData, byte[] signature, byte[]? userHandle)
    {
        var id = Base64Url.EncodeToString(credentialId);
        return JsonSerializer.Serialize(new {
            id,
            rawId = id,
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new {
                clientDataJSON = Base64Url.EncodeToString(clientDataJson),
                authenticatorData = Base64Url.EncodeToString(authenticatorData),
                signature = Base64Url.EncodeToString(signature),
                userHandle = userHandle is null ? null : Base64Url.EncodeToString(userHandle),
            },
        });
    }
}
```

Run the test → PASS.

- [ ] **Step 3: The Apple client**

Create `src/dotnet/App.Maui/MaciOS/Services/ApplePasskeyClient.cs`:

```csharp
using ActualChat.UI.Blazor.Services;
using AuthenticationServices;
using Foundation;

namespace ActualChat.App.Maui;

public sealed class ApplePasskeyClient : IPasskeyClient
{
    public Task<bool> IsAvailable(CancellationToken cancellationToken)
        => Task.FromResult(OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacOSVersionAtLeast(13)
            || OperatingSystem.IsMacCatalystVersionAtLeast(16));

    public async Task<string> Create(string optionsJson, CancellationToken cancellationToken)
    {
        var options = PasskeyJson.ParseCreationOptions(optionsJson);
        var provider = new ASAuthorizationPlatformPublicKeyCredentialProvider(options.RpId);
        var request = provider.CreateCredentialRegistrationRequest(
            NSData.FromArray(options.Challenge), options.UserName, NSData.FromArray(options.UserId));
        request.UserVerificationPreference = ASAuthorizationPublicKeyCredentialUserVerificationPreference.Required;
        var authorization = await Run(request, cancellationToken).ConfigureAwait(false);
        var registration = (ASAuthorizationPlatformPublicKeyCredentialRegistration)authorization.GetCredential<NSObject>();
        return PasskeyJson.Attestation(
            registration.CredentialId.ToArray(),
            registration.RawClientDataJson.ToArray(),
            registration.RawAttestationObject!.ToArray());
    }

    public async Task<string> Get(string optionsJson, CancellationToken cancellationToken)
    {
        var options = PasskeyJson.ParseRequestOptions(optionsJson);
        var provider = new ASAuthorizationPlatformPublicKeyCredentialProvider(options.RpId);
        var request = provider.CreateCredentialAssertionRequest(NSData.FromArray(options.Challenge));
        request.UserVerificationPreference = ASAuthorizationPublicKeyCredentialUserVerificationPreference.Required;
        var authorization = await Run(request, cancellationToken).ConfigureAwait(false);
        var assertion = (ASAuthorizationPlatformPublicKeyCredentialAssertion)authorization.GetCredential<NSObject>();
        return PasskeyJson.Assertion(
            assertion.CredentialId.ToArray(),
            assertion.RawClientDataJson.ToArray(),
            assertion.RawAuthenticatorData.ToArray(),
            assertion.Signature.ToArray(),
            assertion.UserId?.ToArray());
    }

    // Private methods

    private static Task<ASAuthorization> Run(ASAuthorizationRequest request, CancellationToken cancellationToken)
    {
        var handler = new Handler();
        var controller = new ASAuthorizationController([request]) {
            Delegate = handler,
            PresentationContextProvider = handler,
        };
        cancellationToken.Register(() => {
            if (OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacOSVersionAtLeast(13))
                controller.Cancel();
        });
        controller.PerformRequests();
        return handler.WhenCompleted;
    }

    // Nested types

    private sealed class Handler : NSObject, IASAuthorizationControllerDelegate, IASAuthorizationControllerPresentationContextProviding
    {
        private readonly TaskCompletionSource<ASAuthorization> _source = TaskCompletionSourceExt.New<ASAuthorization>();

        public Task<ASAuthorization> WhenCompleted => _source.Task;

        [Export("authorizationController:didCompleteWithAuthorization:")]
        public void DidComplete(ASAuthorizationController controller, ASAuthorization authorization)
            => _source.TrySetResult(authorization);

        [Export("authorizationController:didCompleteWithError:")]
        public void DidComplete(ASAuthorizationController controller, NSError error)
        {
            var isCancelled = error.Domain == ASAuthorizationError.Canceled.GetDomain()
                && error.Code == (long)ASAuthorizationError.Canceled;
            _source.TrySetException(isCancelled
                ? new PasskeyCancelledException()
                : StandardError.External(error.LocalizedDescription));
        }

        public UIKit.UIWindow GetPresentationAnchor(ASAuthorizationController controller)
            => UIKit.UIApplication.SharedApplication.KeyWindow!;
    }
}
```

`GetPresentationAnchor` returns `UIWindow` on iOS/Catalyst and `AppKit.NSWindow` on AppKit macOS — wrap the member in `#if MACOS ... #else ... #endif` (see how `MaciOS/` files handle the split; `AppleFileSaver` or `MacClipboardUI` show the idiom). `ASAuthorizationError.Canceled.GetDomain()` — if the binding lacks `GetDomain`, compare `error.Domain == "com.apple.AuthenticationServices.AuthorizationError"` and `error.Code == 1001`. The delegate selector names must match the binding's protocol; if `IASAuthorizationControllerDelegate` exposes them as optional interface members, drop the `[Export]` attributes.

- [ ] **Step 4: Register on all three Apple targets**

In `MauiProgram.iOS.cs` and `MauiProgram.MacCatalyst.cs`, after `services.AddScoped(c => new NativeAppleAuth(c));`, and in `MauiProgram.MacOS.cs` in the same platform-services method:

```csharp
        services.AddScoped<IPasskeyClient>(_ => new ApplePasskeyClient());
```

- [ ] **Step 5: Entitlements and AASA**

In each of `Platforms/iOS/Entitlements.prod.plist`, `Platforms/MacCatalyst/Entitlements.prod.plist`, `Platforms/MacOS/Entitlements.prod.plist`, inside the `com.apple.developer.associated-domains` array:

```xml
		<string>webcredentials:voxt.ai</string>
```

and in the three `Entitlements.dev.plist` files, next to their existing `applinks:` entries (they list the dev host — mirror it):

```xml
		<string>webcredentials:dev.voxt.ai</string>
```

In `src/dotnet/App.Wasm/wwwroot/.well-known/apple-app-site-association.json`, add a sibling of `applinks`:

```json
  "webcredentials": {
    "apps": [
      "M287G8G83F.chat.actual.app",
      "M287G8G83F.chat.actual.dev.app"
    ]
  }
```

- [ ] **Step 6: Build on the Mac**

Per memory `build-android-locally-ios-via-mba-ssh`, iOS/Catalyst build only on `mba`:

```bash
ssh mba 'cd <repo clone> && git fetch && git checkout feat/passkeys && git pull && dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net11.0-ios'
```

(Push is needed for that — ask the user before pushing.) Expected: success. Device pass on iPhone: Settings → Passkeys → Add → Face ID sheet → "iCloud Keychain" listed; sign out; "Sign in with a passkey" → Face ID → signed in. Then on a desktop browser at dev.voxt.ai: "Sign in with a passkey" → browser QR → phone → signed in (hybrid transport, the free cross-device path from the spec).

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/UI.Blazor/Services/PasskeyUI/PasskeyJson.cs tests/UI.Blazor.IntegrationTests/PasskeyJsonTest.cs src/dotnet/App.Maui src/dotnet/App.Wasm/wwwroot/.well-known/apple-app-site-association.json
git commit -m "feat(apple): passkeys via ASAuthorizationController; webcredentials association"
```

---

### Task 12: AOT helpers, docs, full verification

**Files:**
- Modify (generated): `src/dotnet/Api.Contracts/Module/ApiContractsAotSource.g.cs`, `src/dotnet/UI.Blazor/Module/BlazorUIAotSource.g.cs`, `src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs`
- Modify: `docs/api-index.md`, `docs/api-index-full.md`, `docs/api-index-ts.md`

- [ ] **Step 1: Regenerate AOT sources**

```bash
./update-aot-helpers.cmd
```

Expected: the three `*AotSource.g.cs` files gain `IPasskeyAuth`, `IPasskeysBackend`, the commands, `Passkey`, `PasskeyCredential`, `PasskeySettings`, `PasskeyRenameModal`, `PasskeyStep`. Inspect `git diff --stat` — only `.g.cs` files and the mibc should change.

- [ ] **Step 2: Index the new types**

Add one-liners in `docs/api-index.md` under the Users/auth section (`IPasskeyAuth` — passkey registration/sign-in; `IPasskeysBackend`; `PasskeyUI`; `IPasskeyClient`), the full entries in `docs/api-index-full.md`, and `webauthn-json` + `Passkeys` in `docs/api-index-ts.md`, following each file's existing line format.

- [ ] **Step 3: Full verification**

```bash
npm run build:Verify
dotnet build src/dotnet/App.Server/App.Server.csproj
dotnet test tests/Users.IntegrationTests
dotnet test tests/UI.Blazor.IntegrationTests --filter "FullyQualifiedName~PasskeyJsonTest"
dotnet test tests/Core.UnitTests --filter "FullyQualifiedName~AppLocalizationTest"
npm run test:unit
```

Expected: all green. Then the manual web pass (Task 8 Step 4) once more end-to-end on the final build.

- [ ] **Step 4: Commit and hand over**

```bash
git add -A src/dotnet docs
git commit -m "chore(auth): regenerate AOT helpers and index passkey types"
```

Report: which device passes ran (web/Chrome virtual authenticator, Android, iOS, Windows) and which are still owed; the prod `PasskeyOrigins` value if it couldn't be committed. Then ask whether to run `/prepare-merge` and `/create-pr`.
