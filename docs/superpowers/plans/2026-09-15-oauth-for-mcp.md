# OAuth 2.1 Authorization Server for MCP Clients — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let any MCP client (Claude surfaces, Claude Code, Cursor, …) connect to `/api/mcp` through a standard OAuth "Connect" flow acting as the consenting user, with per-user revocation in Settings.

**Architecture:** OpenIddict (server + validation + EF stores) in a new `OAuth.Service` project with its own `OAuthDbContext`; DCR, CIMD and the RFC 9728 document are hand-written on top. Each user consent = one OpenIddict permanent authorization + one backing `SessionKind.OAuth` session; access tokens are JWTs carrying `sid`, and `McpAuthMiddleware` resolves them to that session so tools run unchanged.

**Tech Stack:** .NET 11 (rc.1), ASP.NET Core MVC controllers, OpenIddict 7.7.0 (`OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore`), EF Core 11 preview + Npgsql, ActualLab.Fusion 14.4, ModelContextProtocol 1.3.0 (tests), xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-15-oauth-for-mcp-design.md`

**Issue:** #4539 (`branch.feat/mcp-enhancements.issue`)

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing any C#/Razor. Key rules: no `Async` suffix; no `///` on members; K&R braces except types/methods; control-flow statements on their own line followed by a blank line; `.ConfigureAwait(false)` in service code; `x.IsNullOrEmpty()`; no `StringComparison.Ordinal`; `sealed` unless proxied by Fusion (`IComputeService` implementations stay unsealed with `virtual` members); tests named `SubjectShouldBehavior`, FluentAssertions, `// arrange / act / assert`.
- Never hardcode user-visible text in `.razor`: every new string goes into `Strings.en.json` **and** the 18 other hand-written catalogs (`bg bs cs de es fr hi id it ja ko pl pt ru tr uk vi zh`), then `scripts/derive-bcms.cmd` + `scripts/derive-max.cmd`; add the typed member to `LocalizedStringsLocalizerExt.cs`. `AppLocalizationTest` (in `Chat.UI.Blazor.UnitTests`) enforces it. Non-English values may be English placeholders marked for translation only if the project already does that — check a recent key (e.g. `SignIn_ToUseChatInvite`) in `Strings.de.json`; otherwise translate.
- Spec deviation (deliberate): the client-facing contract `IOAuthGrants` lives in `Api.Contracts/OAuth/` and its DTOs in `Api/OAuth/`, because that is where every client-callable interface lives (`docs/development/implementing-features.md`). There is **no** `OAuth.Contracts` project — nothing server-side needs a backend contract.
- Lifetimes (spec): authorization code 5 min, access token 1 h, refresh token 90 d rotated, backing session 90 d sliding. Scopes: `mcp`, `offline_access`.
- OpenIddict: `7.7.0`. If its EF stores fail at runtime against EF Core 11 preview (Task 2 step 6 will show it), switch both packages to `8.0.0-preview.4.26456.66` — same API surface for everything used here.
- Every OpenIddict handler/type name written below was taken from OpenIddict 7 public API as known at planning time; if a name doesn't compile, look it up in `~/.nuget/packages/openiddict.server/7.7.0/lib/net10.0/OpenIddict.Server.xml` before inventing an alternative.
- All new endpoints are MVC controllers (not minimal APIs) so `HttpRateLimitMiddleware` sees them.
- Build with `dotnet build src/dotnet/App.Server/App.Server.csproj` (the `.CI.slnf` is stale locally); run tests with `dotnet test tests/<Project>/<Project>.csproj --filter "FullyQualifiedName~<TestClass>"`. Infra (PostgreSQL, Redis, NATS) is already running.
- Commit after every task with a conventional-commit subject (`feat(oauth): …`, `test(oauth): …`) and the attribution trailer `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`. Never push.

---

## File Structure

| Path | Responsibility |
|---|---|
| `src/dotnet/Core/SessionKind.cs`, `SessionExt.cs`, `CoreConstants.cs` | `SessionKind.OAuth`, `@` prefix, `NewOAuth()` |
| `src/dotnet/Api/Users/SessionInfo.cs` | `Kind` derives `OAuth` from the prefix |
| `src/dotnet/Users.Service/AuthHelper.cs`, `SessionsBackend.cs` | refuse OAuth sessions for web sign-in; default expiry |
| `src/dotnet/Chat.Service/Chats.cs` | `isViaApi` also true for OAuth sessions (robot icon) |
| `src/dotnet/Api/OAuth/OAuthGrant.cs`, `OAuthClientInfo.cs` | DTOs |
| `src/dotnet/Api.Contracts/OAuth/IOAuthGrants.cs` | client-facing API + commands |
| `src/dotnet/Api.Contracts/Module/ApiContractsModule.cs` | `fusion.AddClient<IOAuthGrants>()` |
| `src/dotnet/OAuth.Service/OAuth.Service.csproj` | new project (`IsServerSideProject`) |
| `src/dotnet/OAuth.Service/Module/OAuthModule.cs`, `OAuthSettings.cs` | DI, OpenIddict configuration, endpoints gate |
| `src/dotnet/OAuth.Service/Module/OAuthApplicationBuilderExt.cs` | `MapOAuth(app)` — nothing to map (controllers), but registers the protected-resource route group; kept for symmetry with `MapMcp` |
| `src/dotnet/OAuth.Service/Db/OAuthDbContext.cs`, `OAuthDbInitializer.cs` | EF context with OpenIddict tables + Fusion ops; scope seeding |
| `src/dotnet/OAuth.Service/OAuthConstants.cs` | scope names, claim names, property keys, routes |
| `src/dotnet/OAuth.Service/Controllers/OAuthRegistrationController.cs` | `POST /oauth/register` (DCR) |
| `src/dotnet/OAuth.Service/Controllers/OAuthController.cs` | `/oauth/authorize`, `/oauth/token` pass-through |
| `src/dotnet/OAuth.Service/Handlers/ServerMetadataExtender.cs` | RFC 8414 additions |
| `src/dotnet/OAuth.Service/Handlers/CimdClientResolver.cs` | JIT app from client metadata URL |
| `src/dotnet/OAuth.Service/Handlers/RevocationSessionHandler.cs` | deactivate session after client-side revoke |
| `src/dotnet/OAuth.Service/LoopbackAwareApplicationManager.cs` | port-agnostic loopback redirect match |
| `src/dotnet/OAuth.Service/OAuthGrants.cs` | `IOAuthGrants` implementation (DbServiceBase) |
| `src/dotnet/OAuth.Service/OAuthBearerAuthenticator.cs` | JWT → `sid` → `Session` |
| `src/dotnet/OAuth.Service/OAuthPruner.cs` | hourly cleanup worker |
| `src/dotnet/OAuth.Service.Migration/…` | design-time factory + `Initial` migration |
| `src/dotnet/Core.Server/Resilience/RateLimitClassAttribute.cs`, `Internal/HttpRateLimitMiddleware.cs` | per-action rate-limit class |
| `src/dotnet/Mcp/Auth/McpAuthMiddleware.cs`, `Mcp/Controllers/McpResourceMetadataController.cs` | 401 shape, RFC 9728 document, JWT branch |
| `src/dotnet/UI.Blazor.App/Pages/OAuthConsentPage.razor` (+ `.css`) | consent screen |
| `src/dotnet/UI.Blazor.App/Components/Settings/ConnectedAppsSettings.razor`, `ApiAndAppsSettings.razor` | grants list; combined tab content |
| `src/dotnet/UI.Blazor.App/Components/Settings/SettingsModal.razor` | tab label + content |
| `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs` | `OAuthGrants` accessor |
| `src/dotnet/App.Server/App.Server.csproj`, `AppHost.Build.cs` | project refs, module order |
| `ActualChat.sln`, `ActualChat.Migrations.slnf` | project registration |
| `tests/OAuth.IntegrationTests/…` | protocol + SDK tests |
| `tests/Mcp.IntegrationTests/AuthTest.cs` | resource-side tests |
| `docs/oauth.md` | living doc + acceptance checklist |

---
### Task 1: `SessionKind.OAuth`

**Files:**
- Modify: `src/dotnet/Core/SessionKind.cs`, `src/dotnet/Core/SessionExt.cs`, `src/dotnet/Core/CoreConstants.cs`
- Modify: `src/dotnet/Api/Users/SessionInfo.cs:21-23`
- Modify: `src/dotnet/Users.Service/AuthHelper.cs:107,136`, `src/dotnet/Users.Service/SessionsBackend.cs:52-54`
- Modify: `src/dotnet/Chat.Service/Chats.cs:483`
- Test: `tests/Core.UnitTests/SessionExtTest.cs` (create)

**Interfaces:**
- Produces: `SessionKind.OAuth`, `CoreConstants.Session.OAuthPrefix = '@'`, `CoreConstants.Session.OAuthExpirationTime = 90 d`, `SessionExt.NewOAuth()`; `session.Kind` / `SessionInfo.Kind` return `OAuth` for `@`-prefixed ids.

- [ ] **Step 1: Write the failing test**

```csharp
namespace ActualChat.Core.UnitTests;

public class SessionExtTest
{
    [Fact]
    public void NewOAuthShouldBeOAuthKind()
    {
        // act
        var session = SessionExt.NewOAuth();

        // assert
        session.Kind.Should().Be(SessionKind.OAuth);
        session.Id[0].Should().Be(CoreConstants.Session.OAuthPrefix);
        SessionExt.IsValidId(session.Id).Should().BeTrue();
    }

    [Fact]
    public void KindShouldFollowPrefix()
    {
        SessionExt.NewApiKey().Kind.Should().Be(SessionKind.ApiKey);
        new Session("abcdefghijklmnopqrstuvwxyz").Kind.Should().Be(SessionKind.Session);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Core.UnitTests/Core.UnitTests.csproj --filter "FullyQualifiedName~SessionExtTest"`
Expected: build error — `SessionKind.OAuth` / `NewOAuth` do not exist.

- [ ] **Step 3: Implement**

`SessionKind.cs`: add `OAuth,` after `ApiKey` in the enum and `SessionKind.OAuth => "OAuth"` in `ToReadable`.

`CoreConstants.cs` (`Session` class): add
```csharp
public const char OAuthPrefix = '@';
public static readonly TimeSpan OAuthExpirationTime = TimeSpan.FromDays(90);
```

`SessionExt.cs`:
```csharp
public static bool IsValidId(string sessionId)
{
    if (sessionId.Length is < CoreConstants.Session.MinIdLength or > CoreConstants.Session.MaxIdLength)
        return false;

    var id = sessionId.AsSpan();
    if (id[0] is CoreConstants.Session.ApiKeyPrefix or CoreConstants.Session.OAuthPrefix)
        id = id[1..];
    return Alphabet.AlphaNumericDash.IsMatch(id);
}

public static Session NewApiKey()
    => new (CoreConstants.Session.ApiKeyPrefix + ApiKeyGenerator.Next());

public static Session NewOAuth()
    => new (CoreConstants.Session.OAuthPrefix + ApiKeyGenerator.Next());

extension(Session session)
{
    public SessionKind Kind
        => session.Id[0] switch {
            CoreConstants.Session.ApiKeyPrefix => SessionKind.ApiKey,
            CoreConstants.Session.OAuthPrefix => SessionKind.OAuth,
            _ => SessionKind.Session,
        };
    // IdPrefix / HasIdPrefix unchanged
}
```

`SessionInfo.cs`: same `switch` on `IdPrefix[0]` (guard `IdPrefix.IsNullOrEmpty()` → `Session`).

`AuthHelper.cs` lines 107 and 136: `if (session.Kind is SessionKind.ApiKey or SessionKind.OAuth)`.

`SessionsBackend.cs` line 52: 
```csharp
ExpiresAt = now + session.Kind switch {
    SessionKind.ApiKey => CoreConstants.Session.ApiKeyExpirationTime,
    SessionKind.OAuth => CoreConstants.Session.OAuthExpirationTime,
    _ => CoreConstants.Session.SessionExpirationTime,
},
```

`Chats.cs` line 483: `var isViaApi = session.Kind is SessionKind.ApiKey or SessionKind.OAuth ? true : (bool?)null;`

`Core.Server/HttpSessionExt.cs` `TryGetSessionFromCookie`: reject the OAuth prefix from cookies too:
`if (sessionId[0] is CoreConstants.Session.ApiKeyPrefix or CoreConstants.Session.OAuthPrefix) return null;`

- [ ] **Step 4: Run the tests**

Run: the Task 1 filter above, then `dotnet test tests/Users.IntegrationTests/Users.IntegrationTests.csproj --filter "FullyQualifiedName~Session"` (existing session tests still pass).
Expected: PASS.

- [ ] **Step 5: Commit** — `feat(core): add SessionKind.OAuth for grant-backed sessions`

---

### Task 2: `OAuth.Service` project, DbContext, OpenIddict wiring, discovery document

**Files:**
- Create: `src/dotnet/OAuth.Service/OAuth.Service.csproj`, `Module/OAuthModule.cs`, `Module/OAuthSettings.cs`, `OAuthConstants.cs`, `Db/OAuthDbContext.cs`, `Db/OAuthDbInitializer.cs`, `Handlers/ServerMetadataExtender.cs`
- Create: `src/dotnet/OAuth.Service.Migration/OAuth.Service.Migration.csproj`, `OAuthDbContextContextFactory.cs`, `Migrations/` (generated)
- Modify: `Directory.Packages.props` (add `OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore` 7.7.0), `ActualChat.sln`, `ActualChat.Migrations.slnf`, `src/dotnet/App.Server/App.Server.csproj`, `src/dotnet/App.Server/AppHost.Build.cs` (module after `UsersServiceModule`, before `McpModule`)
- Create: `tests/OAuth.IntegrationTests/OAuth.IntegrationTests.csproj`, `Collections/OAuthCollection.cs`, `OAuthTestBase.cs`, `DiscoveryTest.cs`

**Interfaces:**
- Produces: `OAuthSettings { Route = "/oauth", SigningCertificateBase64, SigningCertificatePassword, AccessTokenLifetime = 1h, RefreshTokenLifetime = 90d, AuthorizationCodeLifetime = 5m, DcrPruneAge = 24h, CimdCacheAge = 24h, AllowInsecureClientMetadata = false }`; `OAuthConstants` (below); `OAuthDbContext`; `OAuthTestBase` with `Http` (no-redirect `HttpClient`), `GetJson(path)`.
- Consumes: `DbModule.AddDbContextServices`, `HostModule<TSettings>`, `UrlMapper`.

- [ ] **Step 1: Packages, projects, solution**

`Directory.Packages.props` next to the ModelContextProtocol lines:
```xml
<PackageVersion Include="OpenIddict.AspNetCore" Version="7.7.0" />
<PackageVersion Include="OpenIddict.EntityFrameworkCore" Version="7.7.0" />
```

`src/dotnet/OAuth.Service/OAuth.Service.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsServerSideProject>true</IsServerSideProject>
    <RootNamespace>ActualChat.OAuth</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Db\Db.csproj" />
    <ProjectReference Include="..\Api.Contracts\Api.Contracts.csproj" />
    <ProjectReference Include="..\Users.Contracts\Users.Contracts.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="OpenIddict.AspNetCore" />
    <PackageReference Include="OpenIddict.EntityFrameworkCore" />
  </ItemGroup>

</Project>
```

`src/dotnet/OAuth.Service.Migration/OAuth.Service.Migration.csproj` — copy `Invite.Service.Migration.csproj`, reference `..\OAuth.Service\OAuth.Service.csproj`.

`OAuthDbContextContextFactory.cs` — copy `InviteDbContextContextFactory.cs`, rename types, database `ac_dev_oauth`, namespace `ActualChat.OAuth`.

Register: `dotnet sln ActualChat.sln add src/dotnet/OAuth.Service/OAuth.Service.csproj src/dotnet/OAuth.Service.Migration/OAuth.Service.Migration.csproj tests/OAuth.IntegrationTests/OAuth.IntegrationTests.csproj` (test project created in step 5; run the command after it exists). Add the migration project path to `ActualChat.Migrations.slnf`. In `App.Server.csproj` add `<ProjectReference Include="..\OAuth.Service.Migration\OAuth.Service.Migration.csproj" />` next to the other migrations and `<ProjectReference Include="..\OAuth.Service\OAuth.Service.csproj" />` next to `Mcp.csproj`.

- [ ] **Step 2: Constants, settings, DbContext, initializer**

`OAuthConstants.cs`:
```csharp
namespace ActualChat.OAuth;

public static class OAuthConstants
{
    public const string McpScope = "mcp";
    public const string SessionIdClaim = "sid";
    public const string RegisterRoute = "register";
    public const string AuthorizeRoute = "authorize";
    public const string TokenRoute = "token";
    public const string RevokeRoute = "revoke";
    public const string ConsentPath = "/oauth/consent";
    public const string DenyParameter = "voxt_deny";
    public const string McpResourcePath = "/api/mcp";

    public static class Properties
    {
        public const string RegisteredVia = "registered_via";
        public const string RegisteredAt = "registered_at";
        public const string CimdFetchedAt = "cimd_fetched_at";
        public const string SessionId = "sid";
    }

    public static class RegisteredVia
    {
        public const string Dcr = "dcr";
        public const string Cimd = "cimd";
    }
}
```

`Module/OAuthSettings.cs`:
```csharp
namespace ActualChat.OAuth.Module;

public sealed class OAuthSettings
{
    // Empty/null disables the authorization server entirely.
    public string Route { get; set; } = "/oauth";
    // Base64 PFX used for both signing and encryption; empty = ephemeral keys (dev/test only).
    public string SigningCertificateBase64 { get; set; } = "";
    public string SigningCertificatePassword { get; set; } = "";
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(90);
    public TimeSpan DcrPruneAge { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan CimdCacheAge { get; set; } = TimeSpan.FromHours(24);
    public bool AllowInsecureClientMetadata { get; set; }
}
```

`Db/OAuthDbContext.cs` — copy `InviteDbContext` shape: no own entities; `Operations`/`Events` DbSets; in `OnModelCreating` call `model.UseOpenIddict();` **before** `UseSnakeCaseNaming()` so OpenIddict's tables get snake_case too, then the `DbOperation`/`DbEvent` collation lines. `Db/OAuthDbInitializer.cs`:
```csharp
public class OAuthDbInitializer(IServiceProvider services) : DbInitializer<OAuthDbContext>(services)
{
    public override async Task InitializeData(CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        if (await scopes.FindByNameAsync(OAuthConstants.McpScope, cancellationToken).ConfigureAwait(false) is not null)
            return;

        await scopes.CreateAsync(new OpenIddictScopeDescriptor {
            Name = OAuthConstants.McpScope,
            DisplayName = "Read and post in your chats via MCP",
            Resources = { OAuthConstants.McpResourcePath },
        }, cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 3: Module**

`Module/OAuthModule.cs`:
```csharp
using ActualChat.Db.Module;
using ActualChat.OAuth.Db;
using ActualChat.OAuth.Handlers;
using ActualChat.Redis.Module;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.OAuth.Module;

public sealed class OAuthModule(IServiceProvider moduleServices)
    : HostModule<OAuthSettings>(moduleServices), IServerModule
{
    public bool IsEnabled => !Settings.Route.IsNullOrEmpty() && HostInfo.HasRole(HostRole.Api);

    protected override void InjectServices(IServiceCollection services)
    {
        if (!IsEnabled)
            return;

        var route = Settings.Route.TrimEnd('/');
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<OAuthDbContext>(services);
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, OAuthDbInitializer>();
        dbModule.AddDbContextServices<OAuthDbContext>(services);
        // OpenIddict's EF stores take the DbContext itself; DbModule registers only the pooled factory
        services.AddScoped(c => c.GetRequiredService<IDbContextFactory<OAuthDbContext>>().CreateDbContext());

        services.AddOpenIddict()
            .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<OAuthDbContext>())
            .AddServer(o => {
                o.SetAuthorizationEndpointUris($"{route}/{OAuthConstants.AuthorizeRoute}")
                    .SetTokenEndpointUris($"{route}/{OAuthConstants.TokenRoute}")
                    .SetRevocationEndpointUris($"{route}/{OAuthConstants.RevokeRoute}");
                o.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
                o.RequireProofKeyForCodeExchange();
                o.RegisterScopes(OAuthConstants.McpScope, Scopes.OfflineAccess);
                o.SetAuthorizationCodeLifetime(Settings.AuthorizationCodeLifetime)
                    .SetAccessTokenLifetime(Settings.AccessTokenLifetime)
                    .SetRefreshTokenLifetime(Settings.RefreshTokenLifetime);
                o.DisableAccessTokenEncryption();
                o.UseReferenceRefreshTokens();
                AddCredentials(o);
                o.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
                o.AddEventHandler(ServerMetadataExtender.Descriptor);
            })
            .AddValidation(o => {
                o.UseLocalServer();
                o.UseAspNetCore();
            });
        services.AddSingleton(c => new ServerMetadataExtender(c));
    }

    // Private methods

    private void AddCredentials(OpenIddictServerBuilder o)
    {
        if (Settings.SigningCertificateBase64.IsNullOrEmpty()) {
            if (!HostInfo.IsDevelopmentInstance && !HostInfo.IsTested)
                throw StandardError.Configuration("OAuthSettings.SigningCertificateBase64 is required outside development.");

            o.AddEphemeralSigningKey().AddEphemeralEncryptionKey();
            return;
        }

        var certificate = X509CertificateLoader.LoadPkcs12(
            Convert.FromBase64String(Settings.SigningCertificateBase64),
            Settings.SigningCertificatePassword.NullIfEmpty());
        o.AddSigningCertificate(certificate).AddEncryptionCertificate(certificate);
    }
}
```
Notes: `SetIssuer` is intentionally not called — OpenIddict derives the issuer from the request host, which is what makes `local.voxt.ai`, tunnels and dev all work; the RFC 9728 document (Task 3) uses `UrlMapper.BaseUri` for the same reason. If `StandardError.Configuration` doesn't exist use `StandardError.Internal`. `IsDevelopmentInstance` is a `HostInfo` property (see `DbModule`).

`Handlers/ServerMetadataExtender.cs`:
```csharp
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace ActualChat.OAuth.Handlers;

/// <summary>
/// Adds what MCP clients look for and OpenIddict doesn't advertise on its own:
/// the DCR endpoint, CIMD support and the public-client auth method.
/// </summary>
public sealed class ServerMetadataExtender(IServiceProvider services) : IOpenIddictServerHandler<HandleConfigurationRequestContext>
{
    public static readonly OpenIddictServerHandlerDescriptor Descriptor
        = OpenIddictServerHandlerDescriptor.CreateBuilder<HandleConfigurationRequestContext>()
            .UseSingletonHandler<ServerMetadataExtender>()
            .SetOrder(int.MaxValue - 100_000)
            .Build();

    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private OAuthSettings Settings { get; } = services.GetRequiredService<OAuthSettings>();

    public ValueTask HandleAsync(HandleConfigurationRequestContext context)
    {
        var route = Settings.Route.TrimEnd('/');
        context.Metadata["registration_endpoint"] = UrlMapper.ToAbsolute($"{route}/{OAuthConstants.RegisterRoute}");
        context.Metadata["client_id_metadata_document_supported"] = true;
        context.TokenEndpointAuthenticationMethods.Add(ClientAuthenticationMethods.None);
        context.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
        return default;
    }
}
```
`context.Metadata` is `Dictionary<string, OpenIddictParameter>`; `true` and `string` convert implicitly. If `TokenEndpointAuthenticationMethods`/`CodeChallengeMethods` aren't collections on the context in 7.7, set `context.Metadata[Metadata.TokenEndpointAuthMethodsSupported]` / `[Metadata.CodeChallengeMethodsSupported]` as `OpenIddictParameter` string arrays instead, merging with any existing value.

`AppHost.Build.cs`: `new OAuthModule(moduleServices),` right after `new UsersServiceModule(moduleServices),`. `using ActualChat.OAuth.Module;`.

- [ ] **Step 4: Migration**

```bash
dotnet ef migrations add Initial \
  --project src/dotnet/OAuth.Service.Migration --startup-project src/dotnet/OAuth.Service.Migration \
  --context OAuthDbContext --output-dir Migrations
```
Inspect: tables `open_iddict_entity_framework_core_applications` (or however snake_case renders — that is fine, nothing external depends on the names), `_operations`, `_events`. Commit the generated files.

- [ ] **Step 5: Test project + first test**

`tests/OAuth.IntegrationTests/OAuth.IntegrationTests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Testing.Host\Testing.Host.csproj" />
    <ProjectReference Include="..\..\src\dotnet\OAuth.Service\OAuth.Service.csproj" />
    <ProjectReference Include="..\..\src\dotnet\Mcp\Mcp.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" />
  </ItemGroup>

</Project>
```
Add `System.IdentityModel.Tokens.Jwt` to `Directory.Packages.props` if absent (latest 8.x). Copy `tests/Mcp.IntegrationTests/xunit.runner.json`-style files if that project has any (check `tests/template.xunit.runner.json` usage in `Mcp.IntegrationTests.csproj`).

`Collections/OAuthCollection.cs` — copy `McpCollection`, name `"oauth"`, and shorten the access-token lifetime for tests:
```csharp
public class AppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("oauth", messageSink, TestAppHostOptions.Default with {
        ConfigureHost = (_, cfg) => cfg.AddInMemory<OAuthSettings>(
            (x => x.AccessTokenLifetime, "00:00:03"),
            (x => x.AllowInsecureClientMetadata, "true")),
    });
```
(`AddInMemory<T>` is the helper `TestAppHostFactory` already uses; `using Microsoft.Extensions.Configuration;` + the helper's namespace.)

`OAuthTestBase.cs`:
```csharp
public abstract class OAuthTestBase<TFixture>(TFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<TFixture>(fixture, @out)
    where TFixture : AppHostFixture
{
    protected WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    protected Uri BaseUri => Tester.UrlMapper.BaseUri;
    protected HttpClient Http => field ??= new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) {
        BaseAddress = BaseUri,
    };

    protected override async Task DisposeAsync()
    {
        Http.Dispose();
        await Tester.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected async Task<JsonElement> GetJson(string path)
    {
        var response = await Http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}
```
(`field` in a lazy property is fine here; the base `DisposeAsync` pattern mirrors `McpTestBase`.)

`DiscoveryTest.cs`:
```csharp
[Collection(nameof(OAuthCollection))]
public class DiscoveryTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData("/.well-known/oauth-authorization-server")]
    [InlineData("/.well-known/openid-configuration")]
    public async Task DiscoveryDocumentShouldAdvertiseMcpRequirements(string path)
    {
        // act
        var doc = await GetJson(path);

        // assert
        doc.GetProperty("authorization_endpoint").GetString().Should().Be(new Uri(BaseUri, "/oauth/authorize").ToString());
        doc.GetProperty("token_endpoint").GetString().Should().Be(new Uri(BaseUri, "/oauth/token").ToString());
        doc.GetProperty("registration_endpoint").GetString().Should().Be(new Uri(BaseUri, "/oauth/register").ToString());
        doc.GetProperty("client_id_metadata_document_supported").GetBoolean().Should().BeTrue();
        doc.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(x => x.GetString()).Should().Contain("S256");
        doc.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(x => x.GetString()).Should().Contain("none");
        doc.GetProperty("scopes_supported").EnumerateArray().Select(x => x.GetString()).Should().Contain(["mcp", "offline_access"]);
        doc.GetProperty("grant_types_supported").EnumerateArray().Select(x => x.GetString()).Should().Contain(["authorization_code", "refresh_token"]);
    }
}
```

- [ ] **Step 6: Run**

`dotnet test tests/OAuth.IntegrationTests/OAuth.IntegrationTests.csproj --filter "FullyQualifiedName~DiscoveryTest"` → PASS. If the app host fails at startup inside OpenIddict's EF store (EF 11 incompatibility), switch the two package versions to `8.0.0-preview.4.26456.66`, regenerate the migration, re-run.

- [ ] **Step 7: Commit** — `feat(oauth): OAuth.Service project with OpenIddict server, own DbContext and discovery document`

---
### Task 3: MCP as a protected resource — RFC 9728 document and 401 shape

**Files:**
- Create: `src/dotnet/Mcp/Controllers/McpResourceMetadataController.cs`
- Modify: `src/dotnet/Mcp/Auth/McpAuthMiddleware.cs`
- Modify: `tests/Mcp.IntegrationTests/AuthTest.cs`

**Interfaces:**
- Produces: `GET /.well-known/oauth-protected-resource` and `GET /.well-known/oauth-protected-resource/api/mcp` → `{ resource, authorization_servers, scopes_supported, bearer_methods_supported }`; every 401 from `/api/mcp` carries `WWW-Authenticate: Bearer realm="Voxt", resource_metadata="<base>/.well-known/oauth-protected-resource/api/mcp", scope="mcp"[, error="…", error_description="…"]`.

- [ ] **Step 1: Failing tests** (add to `AuthTest`)

```csharp
[Theory]
[InlineData("/.well-known/oauth-protected-resource")]
[InlineData("/.well-known/oauth-protected-resource/api/mcp")]
public async Task ProtectedResourceDocumentShouldNameMcpEndpoint(string path)
{
    // arrange
    using var http = Tester.AppHost.NewHttpClient();

    // act
    var doc = JsonDocument.Parse(await http.GetStringAsync(path)).RootElement;

    // assert
    var baseUri = Tester.UrlMapper.BaseUri;
    doc.GetProperty("resource").GetString().Should().Be(new Uri(baseUri, "/api/mcp").ToString());
    doc.GetProperty("authorization_servers")[0].GetString().Should().Be(baseUri.ToString().TrimEnd('/'));
    doc.GetProperty("scopes_supported")[0].GetString().Should().Be("mcp");
    doc.GetProperty("bearer_methods_supported")[0].GetString().Should().Be("header");
}

[Fact]
public async Task MissingHeaderShouldPointAtResourceMetadata()
{
    var response = await SendInitialize(authorization: null);
    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    var header = response.Headers.WwwAuthenticate.ToString();
    header.Should().Contain("resource_metadata=\"").And.Contain("/.well-known/oauth-protected-resource/api/mcp\"");
    header.Should().Contain("scope=\"mcp\"");
    header.Should().NotContain("error=", because: "no token was presented, so this is not an invalid_token case");
}

[Fact]
public async Task MalformedTokenShouldReportInvalidToken()
{
    var response = await SendInitialize(authorization: "Bearer not-a-valid-session-id");
    response.Headers.WwwAuthenticate.ToString().Should().Contain("error=\"invalid_token\"");
}
```

- [ ] **Step 2: Run** `dotnet test tests/Mcp.IntegrationTests/Mcp.IntegrationTests.csproj --filter "FullyQualifiedName~AuthTest"` → the three new tests fail (404 / header missing).

- [ ] **Step 3: Implement**

`McpResourceMetadataController.cs`:
```csharp
using ActualChat.Mcp.Module;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Mcp.Controllers;

[ApiController]
public sealed class McpResourceMetadataController(IServiceProvider services) : ControllerBase
{
    public const string Route = "/.well-known/oauth-protected-resource";

    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private McpSettings Settings { get; } = services.GetRequiredService<McpSettings>();

    [HttpGet(Route), HttpGet(Route + "/api/mcp")]
    public ActionResult Get()
    {
        if (Settings.Route.IsNullOrEmpty())
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=3600";
        return Ok(new {
            resource = UrlMapper.ToAbsolute(Settings.Route),
            authorization_servers = new[] { UrlMapper.BaseUrl.TrimEnd('/') },
            scopes_supported = new[] { "mcp" },
            bearer_methods_supported = new[] { "header" },
        });
    }
}
```
The second `HttpGet` template must match `Settings.Route` exactly; it is a literal because attribute templates are compile-time — if `McpSettings.Route` is ever changed the controller must follow. (`UrlMapper.BaseUrl` ends with `/` — verify with `UrlMapper` source and trim accordingly.)

`McpAuthMiddleware.cs` — replace `Reject`:
```csharp
private Task Reject(HttpContext httpContext, string? error, string description)
{
    var metadataUrl = UrlMapper.ToAbsolute(McpResourceMetadataController.Route + Settings.Route);
    var header = $"Bearer realm=\"{Realm}\", resource_metadata=\"{metadataUrl}\", scope=\"mcp\"";
    if (error is not null)
        header += $", error=\"{error}\", error_description=\"{description}\"";
    httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
    httpContext.Response.Headers[HeaderNames.WWWAuthenticate] = header;
    return httpContext.Response.WriteAsync(description);
}
```
Inject `UrlMapper` and `McpSettings` via the middleware constructor (`(RequestDelegate next, IServiceProvider services)` — resolve `ISessionsBackend`, `UrlMapper`, `McpSettings` from it as properties). Call sites: missing/malformed header → `Reject(ctx, null, "Missing or malformed Authorization: Bearer header.")`; everything else → `error: "invalid_token"`. The "Token is not an API key" branch stays for now; Task 8 replaces it with the JWT path.

- [ ] **Step 4: Run** the `AuthTest` filter → PASS (all, including the pre-existing ones).

- [ ] **Step 5: Commit** — `feat(mcp): publish protected-resource metadata and point 401s at it`

---

### Task 4: Dynamic Client Registration + per-action rate-limit class

**Files:**
- Create: `src/dotnet/Core.Server/Resilience/RateLimitClassAttribute.cs`
- Modify: `src/dotnet/Core.Server/Resilience/Internal/HttpRateLimitMiddleware.cs:26-28`
- Create: `src/dotnet/OAuth.Service/Controllers/OAuthRegistrationController.cs`
- Create: `src/dotnet/OAuth.Service/OAuthApplications.cs`
- Test: `tests/OAuth.IntegrationTests/RegistrationTest.cs`

**Interfaces:**
- Produces: `POST /oauth/register` (RFC 7591); `OAuthApplications` (singleton helper) with `static bool IsValidRedirectUri(string uri, bool allowInsecure)`, `static bool IsLoopback(Uri uri)`, `OpenIddictApplicationDescriptor NewPublicClient(string clientId, string displayName, IEnumerable<string> redirectUris, string registeredVia)`.
- Produces: `[RateLimitClass(RateLimitClass.Auth)]` usable on any controller action.

- [ ] **Step 1: Failing tests**

`RegistrationTest.cs`:
```csharp
[Collection(nameof(OAuthCollection))]
public class RegistrationTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task RegisterShouldIssuePublicClientId()
    {
        // act
        var (status, doc) = await Register(new {
            client_name = "Test Client",
            redirect_uris = new[] { "https://client.example/cb", "http://localhost/cb" },
            grant_types = new[] { "authorization_code", "refresh_token" },
            token_endpoint_auth_method = "none",
        });

        // assert
        status.Should().Be(HttpStatusCode.Created);
        doc.GetProperty("client_id").GetString().Should().NotBeNullOrEmpty();
        doc.GetProperty("client_name").GetString().Should().Be("Test Client");
        doc.GetProperty("token_endpoint_auth_method").GetString().Should().Be("none");
        doc.GetProperty("redirect_uris").GetArrayLength().Should().Be(2);
        doc.GetProperty("client_id_issued_at").GetInt64().Should().BePositive();
        doc.TryGetProperty("client_secret", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("http://client.example/cb", "invalid_redirect_uri")]      // plain http, not loopback
    [InlineData("https://client.example/cb#frag", "invalid_redirect_uri")]
    public async Task RegisterShouldRejectBadRedirectUris(string uri, string error)
    {
        var (status, doc) = await Register(new { redirect_uris = new[] { uri }, token_endpoint_auth_method = "none" });
        status.Should().Be(HttpStatusCode.BadRequest);
        doc.GetProperty("error").GetString().Should().Be(error);
    }

    [Fact]
    public async Task RegisterShouldRejectConfidentialClients()
    {
        var (status, doc) = await Register(new { redirect_uris = new[] { "https://c.example/cb" }, token_endpoint_auth_method = "client_secret_post" });
        status.Should().Be(HttpStatusCode.BadRequest);
        doc.GetProperty("error").GetString().Should().Be("invalid_client_metadata");
    }

    [Fact]
    public async Task RegisterShouldRejectUnknownGrantTypes()
    {
        var (status, doc) = await Register(new { redirect_uris = new[] { "https://c.example/cb" }, grant_types = new[] { "implicit" } });
        status.Should().Be(HttpStatusCode.BadRequest);
        doc.GetProperty("error").GetString().Should().Be("invalid_client_metadata");
    }

    protected async Task<(HttpStatusCode, JsonElement)> Register(object body)
    {
        var response = await Http.PostAsJsonAsync("/oauth/register", body);
        var json = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(json).RootElement);
    }
}
```
Move `Register` into `OAuthTestBase` (later tasks reuse it) as `protected Task<(HttpStatusCode, JsonElement)> Register(object body)` plus `protected async Task<string> RegisterClient(params string[] redirectUris)` returning the `client_id` (defaults: `client_name = "Test Client"`, both grant types, `none`).

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~RegistrationTest"` → 404s.

- [ ] **Step 3: Rate-limit class attribute**

`RateLimitClassAttribute.cs`:
```csharp
namespace ActualChat.Resilience;

/// <summary>
/// Overrides the <see cref="RateLimitClass"/> <c>HttpRateLimitMiddleware</c> charges a controller action to;
/// without it GET/HEAD are <see cref="RateLimitClass.HttpRead"/> and everything else <see cref="RateLimitClass.Command"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RateLimitClassAttribute(RateLimitClass rateLimitClass) : Attribute
{
    public RateLimitClass RateLimitClass { get; } = rateLimitClass;
}
```
`HttpRateLimitMiddleware.cs` line 26:
```csharp
var rateLimitClass = action.MethodInfo.GetCustomAttribute<RateLimitClassAttribute>()?.RateLimitClass
    ?? (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
        ? RateLimitClass.HttpRead
        : RateLimitClass.Command);
```
(`using System.Reflection;`.) `RateLimitClass.Auth` already has an IP budget (600 / 5 min) — that is the register limit.

- [ ] **Step 4: `OAuthApplications` helper**

```csharp
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.OAuth;

public static class OAuthApplications
{
    public static readonly string[] AllowedGrantTypes = [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken];

    public static bool IsLoopback(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttp
            && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    public static bool IsValidRedirectUri(string value, bool allowInsecure)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.Fragment.IsNullOrEmpty())
            return false;

        return uri.Scheme == Uri.UriSchemeHttps || IsLoopback(uri) || (allowInsecure && uri.Scheme == Uri.UriSchemeHttp);
    }

    public static OpenIddictApplicationDescriptor NewPublicClient(
        string clientId, string displayName, IEnumerable<string> redirectUris, string registeredVia)
    {
        var descriptor = new OpenIddictApplicationDescriptor {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = displayName,
            Permissions = {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + OAuthConstants.McpScope,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        foreach (var uri in redirectUris)
            descriptor.RedirectUris.Add(new Uri(uri));
        descriptor.Properties[OAuthConstants.Properties.RegisteredVia] = JsonSerializer.SerializeToElement(registeredVia);
        descriptor.Properties[OAuthConstants.Properties.RegisteredAt] = JsonSerializer.SerializeToElement(DateTime.UtcNow);
        return descriptor;
    }
}
```
(`Permissions.Endpoints.Revocation` may be spelled `Permissions.Endpoints.Revocation` or `...Revoke` — check the constants file; `Permissions.Endpoints.Authorization` exists in 7.x, older docs say `Authorization`.)

- [ ] **Step 5: Registration controller**

```csharp
using ActualChat.OAuth.Module;
using ActualChat.Resilience;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.OAuth.Controllers;

[ApiController]
public sealed class OAuthRegistrationController(IServiceProvider services) : ControllerBase
{
    private IOpenIddictApplicationManager Applications { get; } = services.GetRequiredService<IOpenIddictApplicationManager>();
    private OAuthSettings Settings { get; } = services.GetRequiredService<OAuthSettings>();
    private ILogger Log { get; } = services.LogFor<OAuthRegistrationController>();

    [HttpPost("/oauth/" + OAuthConstants.RegisterRoute)]
    [RateLimitClass(RateLimitClass.Auth)]
    public async Task<ActionResult> Register([FromBody] RegistrationRequest request, CancellationToken cancellationToken)
    {
        if (request.RedirectUris is not { Length: > 0 })
            return Error("invalid_redirect_uri", "redirect_uris is required.");
        if (request.RedirectUris.Any(u => !OAuthApplications.IsValidRedirectUri(u, Settings.AllowInsecureClientMetadata)))
            return Error("invalid_redirect_uri", "Redirect URIs must be https, or http loopback, and carry no fragment.");
        if (!request.TokenEndpointAuthMethod.IsNullOrEmpty() && request.TokenEndpointAuthMethod != ClientAuthenticationMethods.None)
            return Error("invalid_client_metadata", "Only public clients (token_endpoint_auth_method=none) are supported.");
        var grantTypes = request.GrantTypes is { Length: > 0 } ? request.GrantTypes : OAuthApplications.AllowedGrantTypes;
        if (grantTypes.Any(g => !OAuthApplications.AllowedGrantTypes.Contains(g)))
            return Error("invalid_client_metadata", "Only authorization_code and refresh_token grant types are supported.");

        var clientId = RandomStringGenerator.Default.Next(24);
        var name = request.ClientName.NullIfEmpty() ?? "Unnamed client";
        var descriptor = OAuthApplications.NewPublicClient(clientId, name, request.RedirectUris, OAuthConstants.RegisteredVia.Dcr);
        await Applications.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("DCR: registered client {ClientId} ({ClientName}) from {IP}", clientId, name, HttpContext.GetRemoteIPAddress());

        return StatusCode(StatusCodes.Status201Created, new RegistrationResponse {
            ClientId = clientId,
            ClientIdIssuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ClientName = name,
            RedirectUris = request.RedirectUris,
            GrantTypes = grantTypes,
            ResponseTypes = [ResponseTypes.Code],
            TokenEndpointAuthMethod = ClientAuthenticationMethods.None,
            Scope = OAuthConstants.McpScope + " " + Scopes.OfflineAccess,
        });
    }

    // Private methods

    private ActionResult Error(string error, string description)
        => BadRequest(new { error, error_description = description });

    // Nested types

    public sealed record RegistrationRequest
    {
        [JsonPropertyName("client_name")] public string? ClientName { get; init; }
        [JsonPropertyName("redirect_uris")] public string[]? RedirectUris { get; init; }
        [JsonPropertyName("grant_types")] public string[]? GrantTypes { get; init; }
        [JsonPropertyName("token_endpoint_auth_method")] public string? TokenEndpointAuthMethod { get; init; }
    }

    public sealed record RegistrationResponse
    {
        [JsonPropertyName("client_id")] public required string ClientId { get; init; }
        [JsonPropertyName("client_id_issued_at")] public required long ClientIdIssuedAt { get; init; }
        [JsonPropertyName("client_name")] public required string ClientName { get; init; }
        [JsonPropertyName("redirect_uris")] public required string[] RedirectUris { get; init; }
        [JsonPropertyName("grant_types")] public required string[] GrantTypes { get; init; }
        [JsonPropertyName("response_types")] public required string[] ResponseTypes { get; init; }
        [JsonPropertyName("token_endpoint_auth_method")] public required string TokenEndpointAuthMethod { get; init; }
        [JsonPropertyName("scope")] public required string Scope { get; init; }
    }
}
```
Use `"/oauth/"` literal in the route attribute (attribute templates can't read settings; `OAuthSettings.Route` other than `/oauth` is unsupported for controllers — note this in `OAuthSettings` with a one-line comment). `HttpContext.GetRemoteIPAddress()` exists in Core.Server (used by `AuthHelper`).

- [ ] **Step 6: Run** the registration filter → PASS. Also run `--filter "FullyQualifiedName~RateLimit"` in `Core.Server.UnitTests` if such tests exist → still PASS.

- [ ] **Step 7: Commit** — `feat(oauth): dynamic client registration endpoint`

---
### Task 5: `IOAuthGrants` — consent as a permanent authorization + backing session

**Files:**
- Create: `src/dotnet/Api/OAuth/OAuthGrant.cs`, `src/dotnet/Api/OAuth/OAuthClientInfo.cs`
- Create: `src/dotnet/Api.Contracts/OAuth/IOAuthGrants.cs`
- Modify: `src/dotnet/Api.Contracts/Module/ApiContractsModule.cs` (`fusion.AddClient<IOAuthGrants>();` in a new `// OAuth` group after `// Invite`)
- Create: `src/dotnet/OAuth.Service/OAuthGrants.cs`
- Modify: `src/dotnet/OAuth.Service/Module/OAuthModule.cs` (`rpcHost.AddApi<IOAuthGrants, OAuthGrants>()`)
- Test: `tests/OAuth.IntegrationTests/GrantsTest.cs`
- Regenerate: `run-aot-type-generator.cmd` (updates `ApiContractsAotSource.g.cs`)

**Interfaces:**
- Produces (exact):
```csharp
[DataContract, MessagePackObject]
public sealed partial record OAuthGrant(
    [property: DataMember, Key(0)] string Id,           // OpenIddict authorization id
    [property: DataMember, Key(1)] string ClientId,
    [property: DataMember, Key(2)] string ClientName,
    [property: DataMember, Key(3)] ApiArray<string> Scopes,
    [property: DataMember, Key(4)] Moment CreatedAt,
    [property: DataMember, Key(5)] Moment LastUsedAt,
    [property: DataMember, Key(6)] Moment ExpiresAt);

[DataContract, MessagePackObject]
public sealed partial record OAuthClientInfo(
    [property: DataMember, Key(0)] string ClientId,
    [property: DataMember, Key(1)] string ClientName,
    [property: DataMember, Key(2)] ApiArray<string> RedirectHosts,
    [property: DataMember, Key(3)] bool IsLoopback,
    [property: DataMember, Key(4)] ApiArray<string> Scopes);

public interface IOAuthGrants : IComputeService
{
    [ComputeMethod] Task<ApiArray<OAuthGrant>> List(Session session, CancellationToken cancellationToken);
    [ComputeMethod] Task<OAuthClientInfo?> GetClient(Session session, string clientId, CancellationToken cancellationToken);
    [CommandHandler] Task<string> OnApprove(OAuthGrants_Approve command, CancellationToken cancellationToken);
    [CommandHandler] Task OnRevoke(OAuthGrants_Revoke command, CancellationToken cancellationToken);
}
// OAuthGrants_Approve : ApiCommand<string> { [Key(2)] required string ClientId; [Key(3)] required ApiArray<string> Scopes }
// OAuthGrants_Revoke  : ApiCommand<Unit>   { [Key(2)] required string AuthorizationId }
```
- Server-internal (used by Task 6): `OAuthGrants.FindAuthorization(UserId userId, string applicationId, IReadOnlyCollection<string> scopes, CancellationToken)` → `object?` (OpenIddict authorization) and `OAuthGrants.GetSessionId(object authorization, CancellationToken)` → `string?`, `OAuthGrants.TouchSession(string sessionId, CancellationToken)`.

- [ ] **Step 1: Failing tests**

```csharp
[Collection(nameof(OAuthCollection))]
public class GrantsTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ApproveShouldCreateGrantAndBackingSession()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("https://c.example/cb");
        var grants = Tester.AppServices.GetRequiredService<IOAuthGrants>();

        // act
        var authorizationId = await Tester.Commander.Call(new OAuthGrants_Approve {
            Session = Tester.Session, ClientId = clientId, Scopes = ["mcp", "offline_access"],
        });
        var list = await ComputedTest.When(async ct => {
            var l = await grants.List(Tester.Session, ct);
            l.Should().ContainSingle(g => g.Id == authorizationId);
            return l;
        }, TimeSpan.FromSeconds(10));

        // assert
        var grant = list.Single(g => g.Id == authorizationId);
        grant.ClientName.Should().Be("Test Client");
        grant.Scopes.Should().BeEquivalentTo(["mcp", "offline_access"]);
        var sessions = await Tester.AppServices.GetRequiredService<IAccountsBackend>().ListSessions(alice.Id, default);
        sessions.Should().ContainSingle(s => s.Kind == SessionKind.OAuth);
        var apiKeys = await Tester.AppServices.GetRequiredService<IAccounts>().ListOwnSessions(Tester.Session, SessionKind.ApiKey, default);
        apiKeys.Should().BeEmpty(because: "OAuth sessions must not show up as API keys");
    }

    [Fact]
    public async Task ApproveShouldBeIdempotent()
    {
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("https://c.example/cb");
        var cmd = new OAuthGrants_Approve { Session = Tester.Session, ClientId = clientId, Scopes = ["mcp"] };
        var a = await Tester.Commander.Call(cmd);
        var b = await Tester.Commander.Call(cmd with { Uuid = ApiCommand.NewUuid() });
        b.Should().Be(a);
    }

    [Fact]
    public async Task ApproveShouldRejectGuests()
    {
        var clientId = await RegisterClient("https://c.example/cb");
        var call = () => Tester.Commander.Call(new OAuthGrants_Approve { Session = Tester.Session, ClientId = clientId, Scopes = ["mcp"] });
        await call.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task RevokeShouldDeactivateSessionAndHideGrant()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("https://c.example/cb");
        var grants = Tester.AppServices.GetRequiredService<IOAuthGrants>();
        var id = await Tester.Commander.Call(new OAuthGrants_Approve { Session = Tester.Session, ClientId = clientId, Scopes = ["mcp"] });

        // act
        await Tester.Commander.Call(new OAuthGrants_Revoke { Session = Tester.Session, AuthorizationId = id });

        // assert
        await ComputedTest.When(async ct => (await grants.List(Tester.Session, ct)).Should().NotContain(g => g.Id == id), TimeSpan.FromSeconds(10));
        var backend = Tester.AppServices.GetRequiredService<IAccountsBackend>();
        var sessionsBackend = Tester.AppServices.GetRequiredService<ISessionsBackend>();
        foreach (var s in (await backend.ListSessions(alice.Id, default)).Where(s => s.Kind == SessionKind.OAuth))
            (await sessionsBackend.Get(s, default))!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetClientShouldDescribeRegisteredClient()
    {
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("http://localhost/cb", "https://c.example/cb");
        var info = await Tester.AppServices.GetRequiredService<IOAuthGrants>().GetClient(Tester.Session, clientId, default);
        info.Should().NotBeNull();
        info!.ClientName.Should().Be("Test Client");
        info.IsLoopback.Should().BeTrue();
        info.RedirectHosts.Should().Contain("c.example");
    }
}
```
(`Tester.AppServices` — check `WebClientTester` exposes it; `McpTestBase` uses `Tester.Commander` which is `AppServices.Commander()`. `ComputedTest.When` lives in `ActualChat.Testing`; see `tests/Testing/ComputedTest.cs` for the exact overloads.)

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~GrantsTest"` → compile errors.

- [ ] **Step 3: Contracts** — create the records/interface exactly as in *Interfaces*; the commands:
```csharp
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record OAuthGrants_Approve : ApiCommand<string>
{
    [DataMember(Order = 2), Key(2)] public required string ClientId { get; init; }
    [DataMember(Order = 3), Key(3)] public required ApiArray<string> Scopes { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record OAuthGrants_Revoke : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string AuthorizationId { get; init; }
}
```
Namespace `ActualChat.OAuth` for all four files. Register the client in `ApiContractsModule`. Run `run-aot-type-generator.cmd` (or `dotnet run --project src/dotnet/App.AotHelper -- -g` — see `docs/native-aot.md`) and commit the regenerated `*.g.cs`.

- [ ] **Step 4: Service**

`OAuthGrants.cs`:
```csharp
using ActualChat.OAuth.Db;
using ActualChat.Users;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.OAuth;

public class OAuthGrants(IServiceProvider services) : DbServiceBase<OAuthDbContext>(services), IOAuthGrants
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private ISessionsBackend SessionsBackend { get; } = services.GetRequiredService<ISessionsBackend>();
    private OAuthSettings Settings { get; } = services.GetRequiredService<OAuthSettings>();

    // OpenIddict managers are scoped and this service is a singleton, so every public method opens
    // its own scope and resolves the managers from it.

    // [ComputeMethod]
    public virtual async Task<ApiArray<OAuthGrant>> List(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuest)
            return [];

        return await ListByUser(account.Id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<OAuthClientInfo?> GetClient(Session session, string clientId, CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (application is null)
            return null;

        var redirectUris = (await applications.GetRedirectUrisAsync(application, cancellationToken).ConfigureAwait(false))
            .Select(u => new Uri(u)).ToList();
        var permissions = await applications.GetPermissionsAsync(application, cancellationToken).ConfigureAwait(false);
        var scopes = permissions
            .Where(p => p.StartsWith(Permissions.Prefixes.Scope))
            .Select(p => p[Permissions.Prefixes.Scope.Length..])
            .Append(Scopes.OfflineAccess)
            .ToApiArray();
        return new OAuthClientInfo(
            clientId,
            await applications.GetDisplayNameAsync(application, cancellationToken).ConfigureAwait(false) ?? clientId,
            redirectUris.Select(u => u.Host).Distinct().ToApiArray(),
            redirectUris.Any(OAuthApplications.IsLoopback),
            scopes);
    }

    // [CommandHandler]
    public virtual async Task<string> OnApprove(OAuthGrants_Approve command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            if (context.Operation.Items.Get<UserId?>() is { } userId)
                _ = ListByUser(userId, default);
            return null!;
        }

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);
        account.Require(AccountFull.MustBeActive);
        var scopes = command.Scopes.Where(s => s == OAuthConstants.McpScope || s == Scopes.OfflineAccess).Distinct().ToArray();
        if (!scopes.Contains(OAuthConstants.McpScope))
            throw StandardError.Constraint("The 'mcp' scope is required.");

        using var scope = Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var application = await applications.FindByClientIdAsync(command.ClientId, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.NotFound("OAuth client");
        var applicationId = (await applications.GetIdAsync(application, cancellationToken).ConfigureAwait(false))!;
        var clientName = await applications.GetDisplayNameAsync(application, cancellationToken).ConfigureAwait(false) ?? command.ClientId;

        var existing = await FindAuthorization(account.Id, applicationId, scopes, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return (await authorizations.GetIdAsync(existing, cancellationToken).ConfigureAwait(false))!;

        var backingSession = SessionExt.NewOAuth();
        await Commander.Call(new SessionsBackend_Upsert(backingSession) {
            UserId = account.Id,
            Description = $"{clientName} (OAuth)",
            ExpiresAt = Clocks.SystemClock.Now + Settings.RefreshTokenLifetime,
        }, true, cancellationToken).ConfigureAwait(false);

        var descriptor = new OpenIddictAuthorizationDescriptor {
            ApplicationId = applicationId,
            Subject = account.Id.Value,
            Status = Statuses.Valid,
            Type = AuthorizationTypes.Permanent,
            CreationDate = Clocks.SystemClock.Now.ToDateTimeOffset(),
        };
        foreach (var s in scopes)
            descriptor.Scopes.Add(s);
        descriptor.Properties[OAuthConstants.Properties.SessionId] = JsonSerializer.SerializeToElement(backingSession.Id);
        var authorization = await authorizations.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);

        // Writes went through OpenIddict's own DbContext; this scope exists only to log the operation
        // so ListByUser is invalidated on every host.
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        context.Operation.Items.Set<UserId?>(account.Id);
        return (await authorizations.GetIdAsync(authorization, cancellationToken).ConfigureAwait(false))!;
    }

    // [CommandHandler]
    public virtual async Task OnRevoke(OAuthGrants_Revoke command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            if (context.Operation.Items.Get<UserId?>() is { } userId)
                _ = ListByUser(userId, default);
            return;
        }

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);
        using var scope = Services.CreateScope();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await authorizations.FindByIdAsync(command.AuthorizationId, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.NotFound("OAuth grant");
        if (await authorizations.GetSubjectAsync(authorization, cancellationToken).ConfigureAwait(false) != account.Id.Value)
            throw StandardError.NotFound("OAuth grant");

        await Revoke(scope.ServiceProvider, authorization, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        context.Operation.Items.Set<UserId?>(account.Id);
    }

    // Used by the authorize endpoint, the revocation handler and the pruner
    public async Task<object?> FindAuthorization(UserId userId, string applicationId, IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        return await authorizations
            .FindAsync(userId.Value, applicationId, Statuses.Valid, AuthorizationTypes.Permanent, scopes.ToImmutableArray(), cancellationToken)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string?> GetSessionId(IServiceProvider scopedServices, object authorization, CancellationToken cancellationToken)
    {
        var authorizations = scopedServices.GetRequiredService<IOpenIddictAuthorizationManager>();
        var properties = await authorizations.GetPropertiesAsync(authorization, cancellationToken).ConfigureAwait(false);
        return properties.TryGetValue(OAuthConstants.Properties.SessionId, out var v) ? v.GetString() : null;
    }

    public Task TouchSession(string sessionId, CancellationToken cancellationToken)
        => Commander.Call(new SessionsBackend_Upsert(new Session(sessionId)) {
            ExpiresAt = Clocks.SystemClock.Now + Settings.RefreshTokenLifetime,
        }, true, cancellationToken);

    public async Task Revoke(IServiceProvider scopedServices, object authorization, CancellationToken cancellationToken)
    {
        var authorizations = scopedServices.GetRequiredService<IOpenIddictAuthorizationManager>();
        var tokens = scopedServices.GetRequiredService<IOpenIddictTokenManager>();
        var id = (await authorizations.GetIdAsync(authorization, cancellationToken).ConfigureAwait(false))!;
        await authorizations.TryRevokeAsync(authorization, cancellationToken).ConfigureAwait(false);
        await foreach (var token in tokens.FindByAuthorizationIdAsync(id, cancellationToken).ConfigureAwait(false))
            await tokens.TryRevokeAsync(token, cancellationToken).ConfigureAwait(false);
        var sessionId = await GetSessionId(scopedServices, authorization, cancellationToken).ConfigureAwait(false);
        if (sessionId is not null)
            await Commander.Call(new AccountsBackend_SignOut(new Session(sessionId), Deactivate: true), true, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<ApiArray<OAuthGrant>> ListByUser(UserId userId, CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var result = new List<OAuthGrant>();
        await foreach (var authorization in authorizations.FindBySubjectAsync(userId.Value, cancellationToken).ConfigureAwait(false)) {
            if (await authorizations.GetStatusAsync(authorization, cancellationToken).ConfigureAwait(false) != Statuses.Valid)
                continue;

            var sessionId = await GetSessionId(scope.ServiceProvider, authorization, cancellationToken).ConfigureAwait(false);
            var sessionInfo = sessionId is null ? null
                : await SessionsBackend.Get(new Session(sessionId), cancellationToken).ConfigureAwait(false);
            if (sessionInfo is not { IsActive: true })
                continue;

            var applicationId = await authorizations.GetApplicationIdAsync(authorization, cancellationToken).ConfigureAwait(false);
            var application = applicationId is null ? null
                : await applications.FindByIdAsync(applicationId, cancellationToken).ConfigureAwait(false);
            var clientId = application is null ? "" : await applications.GetClientIdAsync(application, cancellationToken).ConfigureAwait(false) ?? "";
            var clientName = application is null ? clientId : await applications.GetDisplayNameAsync(application, cancellationToken).ConfigureAwait(false) ?? clientId;
            result.Add(new OAuthGrant(
                (await authorizations.GetIdAsync(authorization, cancellationToken).ConfigureAwait(false))!,
                clientId,
                clientName,
                (await authorizations.GetScopesAsync(authorization, cancellationToken).ConfigureAwait(false)).ToApiArray(),
                (await authorizations.GetCreationDateAsync(authorization, cancellationToken).ConfigureAwait(false))?.ToMoment() ?? sessionInfo.CreatedAt,
                sessionInfo.LastSeenAt,
                sessionInfo.ExpiresAt));
        }
        return result.OrderByDescending(g => g.CreatedAt).ToApiArray();
    }
}
```
Notes for the implementer: `ToMoment()` / `ToDateTimeOffset()` conversions exist in ActualLab (`Moment` has `ToDateTimeOffset()`; `DateTimeOffset.ToMoment()` extension — check `ActualLab.Time`); `Clocks` and `Commander` come from `DbServiceBase`; `SessionsBackend.Get` is a Fusion compute method, so `ListByUser` re-evaluates when the session row changes (LastSeenAt) without any manual work; `context.Operation.Items.Set<UserId?>` — `OperationItems` supports typed keys (`Set<T>(T value)`) as used in `SessionsBackend` with string keys; use `Items.Set("UserId", account.Id)` / `Items.Get<UserId?>("UserId")` to mirror it. `Revoke`/`FindAuthorization`/`GetSessionId`/`TouchSession` are public non-virtual helpers — they're not RPC-exposed because they're not on `IOAuthGrants`.

Module: `rpcHost.AddApi<IOAuthGrants, OAuthGrants>();` (create `var rpcHost = services.AddRpcHost(HostInfo);` like `InviteServiceModule`). Also `services.AddSingleton<OAuthGrants>(c => (OAuthGrants)c.GetRequiredService<IOAuthGrants>())` so controllers can take the concrete helper methods — **check** how `AddApi` registers the implementation; if it registers `OAuthGrants` itself, skip this line.

- [ ] **Step 5: Run** `GrantsTest` → PASS.

- [ ] **Step 6: Commit** — `feat(oauth): IOAuthGrants — consent creates a permanent authorization and a backing session`

---
### Task 6: Authorize + token endpoints, loopback redirects, PKCE, refresh rotation

**Files:**
- Create: `src/dotnet/OAuth.Service/Controllers/OAuthController.cs`
- Create: `src/dotnet/OAuth.Service/LoopbackAwareApplicationManager.cs`
- Modify: `src/dotnet/OAuth.Service/Module/OAuthModule.cs` (`AddCore(o => o.UseEntityFrameworkCore()…; o.ReplaceApplicationManager<LoopbackAwareApplicationManager>();)`)
- Test: `tests/OAuth.IntegrationTests/AuthorizationFlowTest.cs`; helpers in `OAuthTestBase.cs`

**Interfaces:**
- Produces: `GET /oauth/authorize` (302 to consent / to client), `POST /oauth/token` (JSON with `access_token`, `refresh_token`, `token_type`, `expires_in`, `scope`), JWT claims `sub`, `sid`, `client_id`, `scope`, `aud`.
- Test helpers: `Pkce NewPkce()` (`Verifier`, `Challenge`), `Task<Uri> Authorize(string clientId, string redirectUri, Pkce pkce, string? state = "s1", string scope = "mcp offline_access", string? resource = null, bool approve = true)` → the final redirect `Location`, `Task<JsonElement> ExchangeCode(clientId, redirectUri, code, verifier, resource = null)`, `Task<(HttpStatusCode, JsonElement)> Refresh(clientId, refreshToken)`, `JwtSecurityToken ReadJwt(string)`.

- [ ] **Step 1: Test helpers** (in `OAuthTestBase`)

```csharp
protected sealed record Pkce(string Verifier, string Challenge);

protected static Pkce NewPkce()
{
    var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    return new Pkce(verifier, challenge);
}

protected string AuthorizeUrl(string clientId, string redirectUri, Pkce pkce, string? state, string scope, string? resource)
{
    var q = new Dictionary<string, string?> {
        ["response_type"] = "code", ["client_id"] = clientId, ["redirect_uri"] = redirectUri,
        ["scope"] = scope, ["state"] = state, ["code_challenge"] = pkce.Challenge, ["code_challenge_method"] = "S256",
        ["resource"] = resource,
    };
    return QueryHelpers.AddQueryString("/oauth/authorize", q.Where(kv => kv.Value is not null));
}

// Drives the consent step the way the Blazor page will: approve via the command, then re-request /oauth/authorize.
protected async Task<HttpResponseMessage> Authorize(
    string clientId, string redirectUri, Pkce pkce, string? state = "s1",
    string scope = "mcp offline_access", string? resource = null, bool approve = true)
{
    var url = AuthorizeUrl(clientId, redirectUri, pkce, state, scope, resource);
    var first = await SendAsUser(HttpMethod.Get, url);
    if (first.StatusCode != HttpStatusCode.Redirect || !first.Headers.Location!.ToString().Contains("/oauth/consent"))
        return first;
    if (!approve)
        return await SendAsUser(HttpMethod.Get, url + "&voxt_deny=1");

    await Tester.Commander.Call(new OAuthGrants_Approve {
        Session = Tester.Session, ClientId = clientId, Scopes = scope.Split(' ').ToApiArray(),
    });
    return await SendAsUser(HttpMethod.Get, url);
}

protected Task<HttpResponseMessage> SendAsUser(HttpMethod method, string url)
{
    var request = new HttpRequestMessage(method, url);
    request.Headers.Add("Cookie", $"{Constants.Session.CookieName}={Tester.Session.Id}");
    return Http.SendAsync(request);
}

protected static string GetQueryValue(Uri location, string name)
    => QueryHelpers.ParseQuery(location.Query).TryGetValue(name, out var v) ? v.ToString() : "";

protected async Task<(HttpStatusCode, JsonElement)> PostToken(Dictionary<string, string> form)
{
    var response = await Http.PostAsync("/oauth/token", new FormUrlEncodedContent(form));
    return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement);
}

protected Task<(HttpStatusCode, JsonElement)> ExchangeCode(string clientId, string redirectUri, string code, string verifier, string? resource = null)
{
    var form = new Dictionary<string, string> {
        ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["redirect_uri"] = redirectUri,
        ["code"] = code, ["code_verifier"] = verifier,
    };
    if (resource is not null)
        form["resource"] = resource;
    return PostToken(form);
}

protected Task<(HttpStatusCode, JsonElement)> Refresh(string clientId, string refreshToken)
    => PostToken(new() { ["grant_type"] = "refresh_token", ["client_id"] = clientId, ["refresh_token"] = refreshToken });

// One-call happy path used by later tasks
protected async Task<(string ClientId, JsonElement Tokens)> ConnectClient(string redirectUri = "https://c.example/cb")
{
    var clientId = await RegisterClient(redirectUri);
    var pkce = NewPkce();
    var response = await Authorize(clientId, redirectUri, pkce);
    var code = GetQueryValue(response.Headers.Location!, "code");
    var (_, tokens) = await ExchangeCode(clientId, redirectUri, code, pkce.Verifier);
    return (clientId, tokens);
}

protected static JwtSecurityToken ReadJwt(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);
```
(`Base64UrlEncoder` from `Microsoft.IdentityModel.Tokens`; `QueryHelpers` from `Microsoft.AspNetCore.WebUtilities` — Testing.Host already references ASP.NET Core.)

- [ ] **Step 2: Failing tests**

```csharp
[Collection(nameof(OAuthCollection))]
public class AuthorizationFlowTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    private const string RedirectUri = "https://c.example/cb";

    [Fact]
    public async Task GuestShouldBeSentToConsentPageWithQueryIntact()
    {
        var clientId = await RegisterClient(RedirectUri);
        var url = AuthorizeUrl(clientId, RedirectUri, NewPkce(), "st", "mcp", null);
        var response = await Http.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("/oauth/consent?");
        location.Should().Contain("client_id=" + clientId).And.Contain("state=st").And.Contain("code_challenge=");
    }

    [Fact]
    public async Task SignedInUserWithoutGrantShouldBeSentToConsent()
    {
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);
        var response = await SendAsUser(HttpMethod.Get, AuthorizeUrl(clientId, RedirectUri, NewPkce(), "st", "mcp", null));
        response.Headers.Location!.ToString().Should().StartWith("/oauth/consent?");
    }

    [Fact]
    public async Task ApprovedRequestShouldRedirectWithCodeAndState()
    {
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);
        var response = await Authorize(clientId, RedirectUri, NewPkce(), state: "xyz");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be(RedirectUri);
        GetQueryValue(location, "code").Should().NotBeEmpty();
        GetQueryValue(location, "state").Should().Be("xyz");
    }

    [Fact]
    public async Task DeniedRequestShouldRedirectWithAccessDenied()
    {
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);
        var response = await Authorize(clientId, RedirectUri, NewPkce(), state: "d", approve: false);
        GetQueryValue(response.Headers.Location!, "error").Should().Be("access_denied");
        GetQueryValue(response.Headers.Location!, "state").Should().Be("d");
    }

    [Fact]
    public async Task UnknownClientShouldNotRedirect()
    {
        await Tester.SignInAsUniqueAlice();
        var response = await SendAsUser(HttpMethod.Get, AuthorizeUrl("nope", RedirectUri, NewPkce(), null, "mcp", null));
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task WrongRedirectUriShouldNotRedirect()
    {
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);
        var response = await SendAsUser(HttpMethod.Get, AuthorizeUrl(clientId, "https://evil.example/cb", NewPkce(), null, "mcp", null));
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task LoopbackRedirectShouldIgnorePort()
    {
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("http://localhost/cb");
        var response = await Authorize(clientId, "http://localhost:53211/cb", NewPkce());
        response.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be("http://localhost:53211/cb");
    }

    [Fact]
    public async Task CodeExchangeShouldIssueJwtWithSessionClaims()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);
        var pkce = NewPkce();
        var code = GetQueryValue((await Authorize(clientId, RedirectUri, pkce)).Headers.Location!, "code");

        // act
        var (status, tokens) = await ExchangeCode(clientId, RedirectUri, code, pkce.Verifier);

        // assert
        status.Should().Be(HttpStatusCode.OK);
        tokens.GetProperty("token_type").GetString().Should().Be("Bearer");
        tokens.GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty();
        var jwt = ReadJwt(tokens.GetProperty("access_token").GetString()!);
        jwt.Subject.Should().Be(alice.Id.Value);
        jwt.Claims.Should().Contain(c => c.Type == "sid" && c.Value.StartsWith("@"));
        jwt.Claims.Should().Contain(c => c.Type == "client_id" && c.Value == clientId);
        jwt.Audiences.Should().Contain(new Uri(BaseUri, "/api/mcp").ToString());
        string.Join(' ', jwt.Claims.Where(c => c.Type == "scope").Select(c => c.Value)).Should().Contain("mcp");
        (jwt.ValidTo - jwt.ValidFrom).Should().BeLessThan(TimeSpan.FromSeconds(10), because: "the test fixture sets a 3s lifetime");
    }

    [Fact]
    public async Task ResourceParameterShouldBecomeAudience()
    {
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);
        var pkce = NewPkce();
        var resource = new Uri(BaseUri, "/api/mcp").ToString();
        var code = GetQueryValue((await Authorize(clientId, RedirectUri, pkce, resource: resource)).Headers.Location!, "code");
        var (_, tokens) = await ExchangeCode(clientId, RedirectUri, code, pkce.Verifier, resource);
        ReadJwt(tokens.GetProperty("access_token").GetString()!).Audiences.Should().Contain(resource);
    }

    [Fact]
    public async Task WrongVerifierShouldBeInvalidGrant()
    {
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);
        var code = GetQueryValue((await Authorize(clientId, RedirectUri, NewPkce())).Headers.Location!, "code");
        var (status, body) = await ExchangeCode(clientId, RedirectUri, code, NewPkce().Verifier);
        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task ReusedCodeShouldBeInvalidGrant()
    {
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient(RedirectUri);
        var pkce = NewPkce();
        var code = GetQueryValue((await Authorize(clientId, RedirectUri, pkce)).Headers.Location!, "code");
        (await ExchangeCode(clientId, RedirectUri, code, pkce.Verifier)).Item1.Should().Be(HttpStatusCode.OK);
        var (status, body) = await ExchangeCode(clientId, RedirectUri, code, pkce.Verifier);
        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task RefreshShouldRotateAndKillOldToken()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (clientId, tokens) = await ConnectClient();
        var oldRefresh = tokens.GetProperty("refresh_token").GetString()!;

        // act
        var (status, refreshed) = await Refresh(clientId, oldRefresh);
        var (replayStatus, replay) = await Refresh(clientId, oldRefresh);

        // assert
        status.Should().Be(HttpStatusCode.OK);
        refreshed.GetProperty("refresh_token").GetString().Should().NotBe(oldRefresh);
        ReadJwt(refreshed.GetProperty("access_token").GetString()!).Claims.Should().Contain(c => c.Type == "sid");
        replayStatus.Should().Be(HttpStatusCode.BadRequest);
        replay.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task RefreshShouldTouchBackingSession()
    {
        var alice = await Tester.SignInAsUniqueAlice();
        var (clientId, tokens) = await ConnectClient();
        var sid = ReadJwt(tokens.GetProperty("access_token").GetString()!).Claims.Single(c => c.Type == "sid").Value;
        var sessionsBackend = Tester.AppServices.GetRequiredService<ISessionsBackend>();
        var before = (await sessionsBackend.Get(new Session(sid), default))!;
        await Task.Delay(1100);
        await Refresh(clientId, tokens.GetProperty("refresh_token").GetString()!);
        var after = await ComputedTest.When(async ct => {
            var s = (await sessionsBackend.Get(new Session(sid), ct))!;
            s.LastSeenAt.Should().BeAfter(before.LastSeenAt);
            return s;
        }, TimeSpan.FromSeconds(10));
        after.ExpiresAt.Should().BeAfter(before.ExpiresAt.Add(TimeSpan.FromSeconds(-1)));
    }
}
```
Note on `RefreshShouldTouchBackingSession`: `SessionsBackend.OnUpsert` only bumps `LastSeenAt` if `MinLastSeenAtUpdatePeriod` passed — read that guard; if it would skip the update, assert on `ExpiresAt` only.

- [ ] **Step 3: Run** → 404 / compile errors.

- [ ] **Step 4: Loopback-aware application manager**

```csharp
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;

namespace ActualChat.OAuth;

/// <summary>
/// RFC 8252 §7.3: a registered loopback redirect (http://localhost/…, http://127.0.0.1/…, http://[::1]/…)
/// matches the same URI on any port — Claude Code and Cursor pick an ephemeral port per session.
/// </summary>
public class LoopbackAwareApplicationManager(
    IOpenIddictApplicationCache<OpenIddictEntityFrameworkCoreApplication> cache,
    ILogger<LoopbackAwareApplicationManager> logger,
    IOptionsMonitor<OpenIddictCoreOptions> options,
    IOpenIddictApplicationStoreResolver resolver)
    : OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication>(cache, logger, options, resolver)
{
    public override async ValueTask<bool> ValidateRedirectUriAsync(
        OpenIddictEntityFrameworkCoreApplication application, string uri, CancellationToken cancellationToken = default)
    {
        if (await base.ValidateRedirectUriAsync(application, uri, cancellationToken).ConfigureAwait(false))
            return true;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var requested) || !OAuthApplications.IsLoopback(requested))
            return false;

        foreach (var registered in await GetRedirectUrisAsync(application, cancellationToken).ConfigureAwait(false)) {
            if (!Uri.TryCreate(registered, UriKind.Absolute, out var candidate) || !OAuthApplications.IsLoopback(candidate))
                continue;
            if (string.Equals(candidate.Host, requested.Host, StringComparison.OrdinalIgnoreCase)
                && candidate.AbsolutePath == requested.AbsolutePath
                && candidate.Query == requested.Query)
                return true;
        }
        return false;
    }
}
```
(Constructor parameter list must match `OpenIddictApplicationManager<T>`'s in 7.7 — copy it from the XML doc/metadata.) Register: `.AddCore(o => { o.UseEntityFrameworkCore().UseDbContext<OAuthDbContext>(); o.ReplaceApplicationManager<LoopbackAwareApplicationManager>(); })`. This manager stays unsealed because OpenIddict resolves it as `OpenIddictApplicationManager<T>`.

- [ ] **Step 5: Controller**

```csharp
using System.Security.Claims;
using ActualChat.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.OAuth.Controllers;

public sealed class OAuthController(IServiceProvider services) : Controller
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private OAuthGrants Grants { get; } = services.GetRequiredService<OAuthGrants>();
    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private ILogger Log { get; } = services.LogFor<OAuthController>();

    [HttpGet("/oauth/" + OAuthConstants.AuthorizeRoute), HttpPost("/oauth/" + OAuthConstants.AuthorizeRoute)]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw StandardError.Internal("OpenIddict request is missing.");
        var session = HttpContext.TryGetSessionFromCookie();
        var account = session is null ? null : await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account is null || account.IsGuest)
            return RedirectToConsent();

        var applications = HttpContext.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.Internal("Client was validated by OpenIddict but is missing.");
        var applicationId = (await applications.GetIdAsync(application, cancellationToken).ConfigureAwait(false))!;

        if (Request.Query.ContainsKey(OAuthConstants.DenyParameter))
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?> {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.AccessDenied,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user denied the request.",
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var scopes = request.GetScopes();
        var authorization = await Grants.FindAuthorization(account.Id, applicationId, scopes, cancellationToken).ConfigureAwait(false);
        if (authorization is null)
            return RedirectToConsent();

        var authorizations = HttpContext.RequestServices.GetRequiredService<IOpenIddictAuthorizationManager>();
        var sessionId = await Grants.GetSessionId(HttpContext.RequestServices, authorization, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.Internal("Authorization has no backing session.");
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, account.Id.Value)
            .SetClaim(Claims.Name, account.Avatar.Name)
            .SetClaim(OAuthConstants.SessionIdClaim, sessionId);
        identity.SetScopes(scopes);
        var resources = request.GetResources();
        identity.SetResources(resources.IsDefaultOrEmpty ? [UrlMapper.ToAbsolute(OAuthConstants.McpResourcePath)] : resources);
        identity.SetAuthorizationId(await authorizations.GetIdAsync(authorization, cancellationToken).ConfigureAwait(false));
        identity.SetDestinations(GetDestinations);
        Log.LogInformation("Authorize: user {UserId} client {ClientId}", account.Id, request.ClientId);
        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        IActionResult RedirectToConsent() {
            var query = Request.QueryString.HasValue ? Request.QueryString.Value : "";
            return Redirect(OAuthConstants.ConsentPath + query);
        }
    }

    [HttpPost("/oauth/" + OAuthConstants.TokenRoute)]
    public async Task<IActionResult> Exchange(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw StandardError.Internal("OpenIddict request is missing.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?> {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnsupportedGrantType,
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        // The principal was stored with the code / refresh token; OpenIddict already validated it
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme).ConfigureAwait(false);
        var principal = result.Principal ?? throw StandardError.Internal("Token principal is missing.");
        var sessionId = principal.GetClaim(OAuthConstants.SessionIdClaim);
        if (sessionId is null)
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?> {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The grant was revoked.",
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (request.IsRefreshTokenGrantType())
            await Grants.TouchSession(sessionId, cancellationToken).ConfigureAwait(false);
        principal.SetDestinations(GetDestinations);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // Private methods

    private static IEnumerable<string> GetDestinations(Claim claim)
        => claim.Type switch {
            Claims.Name or Claims.Subject or OAuthConstants.SessionIdClaim => [Destinations.AccessToken],
            _ => [Destinations.AccessToken],
        };
}
```
Notes: derives from `Controller` (not `ControllerBase`) because `SignIn`/`Forbid` with scheme overloads live there; the `resources.IsDefaultOrEmpty` type is `ImmutableArray<string>` — if `GetResources()` returns `ImmutableArray`, use it as written; the collapsed `GetDestinations` is deliberate (every claim → access token only; no identity token is issued). Revoked grants: the refresh path above relies on OpenIddict rejecting a refresh token whose authorization was revoked (`ValidateAuthorizationEntry`); `RevokeShould…` tests in Task 8 prove it — if they fail, add an explicit check here: look up the authorization by `principal.GetAuthorizationId()` and `Forbid` with `invalid_grant` when its status isn't `Valid`.

- [ ] **Step 6: Run** `AuthorizationFlowTest` → PASS. Then the whole `OAuth.IntegrationTests` project → PASS.

- [ ] **Step 7: Commit** — `feat(oauth): authorize/token endpoints with PKCE, loopback redirects and rotated refresh tokens`

---
### Task 7: CIMD — Client ID Metadata Documents

**Files:**
- Create: `src/dotnet/OAuth.Service/Handlers/CimdClientResolver.cs`
- Modify: `src/dotnet/OAuth.Service/Module/OAuthModule.cs` (register handler + `services.AddHttpClient(CimdClientResolver.HttpClientName, …)`)
- Test: `tests/OAuth.IntegrationTests/CimdTest.cs`, `tests/OAuth.IntegrationTests/CimdTestServer.cs`

**Interfaces:**
- Produces: an authorize request whose `client_id` is an absolute `http(s)://` URL is served by fetching that URL (JSON: `client_id` must equal the URL; `client_name`, `redirect_uris`), creating/updating the application with `registered_via = cimd`; failures → `invalid_client`.
- Consumes: `OAuthApplications.NewPublicClient`, `OAuthSettings.CimdCacheAge`, `AllowInsecureClientMetadata`.

- [ ] **Step 1: Test server + failing tests**

`CimdTestServer.cs` — a tiny `HttpListener` on `WebTestHelpers.GetUnusedLocalUri()` (from Testing.Host) serving one JSON document per path:
```csharp
public sealed class CimdTestServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, string> _documents = new();
    private readonly CancellationTokenSource _stopCts = new();
    private readonly Task _serveTask;
    public int FetchCount;

    public Uri BaseUri { get; }

    public CimdTestServer()
    {
        BaseUri = WebTestHelpers.GetUnusedLocalUri();
        _listener.Prefixes.Add(BaseUri.ToString());
        _listener.Start();
        _serveTask = Task.Run(Serve);
    }

    public string Publish(string name, object document)
    {
        var url = new Uri(BaseUri, $"/cimd/{name}.json").ToString();
        _documents[$"/cimd/{name}.json"] = JsonSerializer.Serialize(document);
        return url;
    }

    public async ValueTask DisposeAsync()
    {
        await _stopCts.CancelAsync();
        _listener.Stop();
        await _serveTask.SilentAwait();
    }

    private async Task Serve()
    {
        while (!_stopCts.IsCancellationRequested) {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) when (_stopCts.IsCancellationRequested) { return; }
            Interlocked.Increment(ref FetchCount);
            if (_documents.TryGetValue(context.Request.Url!.AbsolutePath, out var json)) {
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json));
            }
            else
                context.Response.StatusCode = 404;
            context.Response.Close();
        }
    }
}
```
(If `HttpListener` needs a `+`/`*` prefix on Linux for a `localhost` URL, use `http://127.0.0.1:{port}/` — `GetUnusedLocalUri` returns `http://localhost:…`; adjust.)

`CimdTest.cs`:
```csharp
[Collection(nameof(OAuthCollection))]
public class CimdTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    private const string RedirectUri = "https://claude.example/cb";

    [Fact]
    public async Task UrlClientIdShouldRegisterFromMetadataDocument()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        await using var cimd = new CimdTestServer();
        var clientId = cimd.Publish("claude", new { client_id = "", client_name = "Claude", redirect_uris = new[] { RedirectUri } });
        cimd.Publish("claude", new { client_id = clientId, client_name = "Claude", redirect_uris = new[] { RedirectUri } });

        // act
        var response = await Authorize(clientId, RedirectUri, NewPkce());
        var info = await Tester.AppServices.GetRequiredService<IOAuthGrants>().GetClient(Tester.Session, clientId, default);

        // assert
        response.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(RedirectUri);
        info!.ClientName.Should().Be("Claude");
        info.RedirectHosts.Should().Contain("claude.example");
    }

    [Fact]
    public async Task MismatchedClientIdInsideDocumentShouldBeInvalidClient()
    {
        await Tester.SignInAsUniqueAlice();
        await using var cimd = new CimdTestServer();
        var clientId = cimd.Publish("bad", new { client_id = "https://somewhere.else/x.json", redirect_uris = new[] { RedirectUri } });
        var response = await SendAsUser(HttpMethod.Get, AuthorizeUrl(clientId, RedirectUri, NewPkce(), null, "mcp", null));
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_client");
    }

    [Fact]
    public async Task UnreachableDocumentShouldBeInvalidClient()
    {
        await Tester.SignInAsUniqueAlice();
        var clientId = "http://127.0.0.1:1/cimd/none.json";
        var response = await SendAsUser(HttpMethod.Get, AuthorizeUrl(clientId, RedirectUri, NewPkce(), null, "mcp", null));
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_client");
    }

    [Fact]
    public async Task DocumentShouldBeCachedBetweenRequests()
    {
        await Tester.SignInAsUniqueAlice();
        await using var cimd = new CimdTestServer();
        var clientId = cimd.Publish("cached", new { client_id = "", redirect_uris = new[] { RedirectUri } });
        cimd.Publish("cached", new { client_id = clientId, client_name = "C", redirect_uris = new[] { RedirectUri } });
        await Authorize(clientId, RedirectUri, NewPkce());
        var fetches = cimd.FetchCount;
        await Authorize(clientId, RedirectUri, NewPkce());
        cimd.FetchCount.Should().Be(fetches, because: "the document is cached for CimdCacheAge");
    }
}
```
(The double `Publish` is only to learn the URL before embedding it; fine for a test.) Note: OpenIddict answers an invalid client with its own error page (status 400) since the redirect URI can't be trusted; asserting `invalid_client` in the body is enough.

- [ ] **Step 2: Run** → the first test's authorize returns an OpenIddict `invalid_client` error.

- [ ] **Step 3: Resolver**

```csharp
using System.Net.Http.Json;
using ActualChat.OAuth.Module;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace ActualChat.OAuth.Handlers;

/// <summary>
/// Client ID Metadata Documents (MCP auth spec 2025-11-25): a <c>client_id</c> that is an https URL names
/// a JSON document describing the client. Runs before OpenIddict's client lookup and registers the
/// application just in time, so the built-in validation then finds it like any DCR client.
/// </summary>
public sealed class CimdClientResolver(IServiceProvider services) : IOpenIddictServerHandler<ValidateAuthorizationRequestContext>
{
    public const string HttpClientName = "OAuth.Cimd";
    private const int MaxDocumentLength = 64 * 1024;

    public static readonly OpenIddictServerHandlerDescriptor Descriptor
        = OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateAuthorizationRequestContext>()
            .UseScopedHandler<CimdClientResolver>()
            .SetOrder(OpenIddictServerHandlers.Authentication.ValidateClientId.Descriptor.Order - 1)
            .Build();

    private IOpenIddictApplicationManager Applications { get; } = services.GetRequiredService<IOpenIddictApplicationManager>();
    private IHttpClientFactory HttpClientFactory { get; } = services.GetRequiredService<IHttpClientFactory>();
    private OAuthSettings Settings { get; } = services.GetRequiredService<OAuthSettings>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log { get; } = services.LogFor<CimdClientResolver>();

    public async ValueTask HandleAsync(ValidateAuthorizationRequestContext context)
    {
        var clientId = context.ClientId;
        if (clientId.IsNullOrEmpty() || !Uri.TryCreate(clientId, UriKind.Absolute, out var uri))
            return;
        if (uri.Scheme != Uri.UriSchemeHttps && !(Settings.AllowInsecureClientMetadata && uri.Scheme == Uri.UriSchemeHttp)) {
            context.Reject(Errors.InvalidClient, "Client metadata documents must be served over https.");
            return;
        }

        var cancellationToken = context.CancellationToken;
        var application = await Applications.FindByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (application is not null && !await IsStale(application, cancellationToken).ConfigureAwait(false))
            return;

        var document = await Fetch(uri, cancellationToken).ConfigureAwait(false);
        if (document is null || document.ClientId != clientId) {
            context.Reject(Errors.InvalidClient, "The client metadata document could not be fetched or names a different client_id.");
            return;
        }
        if (document.RedirectUris is not { Length: > 0 }
            || document.RedirectUris.Any(u => !OAuthApplications.IsValidRedirectUri(u, Settings.AllowInsecureClientMetadata))) {
            context.Reject(Errors.InvalidClient, "The client metadata document has no acceptable redirect_uris.");
            return;
        }

        var descriptor = OAuthApplications.NewPublicClient(
            clientId, document.ClientName.NullIfEmpty() ?? uri.Host, document.RedirectUris, OAuthConstants.RegisteredVia.Cimd);
        descriptor.Properties[OAuthConstants.Properties.CimdFetchedAt] = JsonSerializer.SerializeToElement(Clocks.SystemClock.Now.ToDateTime());
        if (application is null)
            await Applications.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        else
            await Applications.UpdateAsync(application, descriptor, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("CIMD: registered client {ClientId} ({ClientName})", clientId, descriptor.DisplayName);
    }

    // Private methods

    private async Task<bool> IsStale(object application, CancellationToken cancellationToken)
    {
        var properties = await Applications.GetPropertiesAsync(application, cancellationToken).ConfigureAwait(false);
        if (!properties.TryGetValue(OAuthConstants.Properties.CimdFetchedAt, out var fetchedAt))
            return true;

        return fetchedAt.GetDateTime() + Settings.CimdCacheAge < Clocks.SystemClock.Now.ToDateTime();
    }

    private async Task<ClientMetadata?> Fetch(Uri uri, CancellationToken cancellationToken)
    {
        try {
            using var http = HttpClientFactory.CreateClient(HttpClientName);
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || (response.Content.Headers.ContentLength ?? 0) > MaxDocumentLength)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return bytes.Length > MaxDocumentLength ? null : JsonSerializer.Deserialize<ClientMetadata>(bytes);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "CIMD: failed to fetch {Uri}", uri);
            return null;
        }
    }

    // Nested types

    private sealed record ClientMetadata
    {
        [JsonPropertyName("client_id")] public string? ClientId { get; init; }
        [JsonPropertyName("client_name")] public string? ClientName { get; init; }
        [JsonPropertyName("redirect_uris")] public string[]? RedirectUris { get; init; }
    }
}
```
Module: `o.AddEventHandler(CimdClientResolver.Descriptor);` and
```csharp
services.AddHttpClient(CimdClientResolver.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
services.AddScoped<CimdClientResolver>();
```
`context.Reject(error, description)` and `context.ClientId` are members of `ValidateAuthorizationRequestContext`; `ValidateClientId` may be named differently in `OpenIddictServerHandlers.Authentication` (e.g. `ValidateClientIdParameter` is the *presence* check, `ValidateClientId` the *existence* check) — pick the existence check. If the descriptor order arithmetic is rejected, use `.SetOrder(OpenIddictServerHandlers.Authentication.ValidateClientIdParameter.Descriptor.Order + 500)`.

- [ ] **Step 4: Run** `CimdTest` → PASS.

- [ ] **Step 5: Commit** — `feat(oauth): resolve client ID metadata documents (CIMD) into just-in-time registrations`

---

### Task 8: JWT bearer on `/api/mcp`

**Files:**
- Create: `src/dotnet/OAuth.Service/OAuthBearerAuthenticator.cs`
- Modify: `src/dotnet/OAuth.Service/Module/OAuthModule.cs` (`services.AddSingleton<OAuthBearerAuthenticator>()`)
- Modify: `src/dotnet/Mcp/Mcp.csproj` (reference `OAuth.Service`), `src/dotnet/Mcp/Auth/McpAuthMiddleware.cs`
- Test: `tests/OAuth.IntegrationTests/McpAccessTest.cs`

**Interfaces:**
- Produces: `OAuthBearerAuthenticator.TryGetSession(HttpContext, CancellationToken) → Task<Session?>`; `/api/mcp` accepts either an API key or an OAuth JWT.

- [ ] **Step 1: Failing tests**

```csharp
[Collection(nameof(OAuthCollection))]
public class McpAccessTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task JwtShouldListToolsAndPostAsUser()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true, title: "OAuth chat");
        var (_, tokens) = await ConnectClient();

        // act
        var client = await CreateMcpClient(tokens.GetProperty("access_token").GetString()!);
        var tools = await client.ListToolsAsync();
        var result = await client.CallToolAsync("post_message", new Dictionary<string, object?> { ["chatId"] = chatId.Value, ["text"] = "via oauth" });

        // assert
        tools.Select(t => t.Name).Should().Contain("post_message");
        result.IsError.Should().NotBe(true);
        var entries = await Tester.AppServices.GetRequiredService<IChats>().GetTile(Tester.Session, chatId, ...); // use the helper Mcp tests use (McpMessageToolsTest) to read the last entry
        // assert the entry exists, AuthorId maps to alice, and IsViaApi == true
    }

    [Fact]
    public async Task ApiKeyShouldStillWork()
    {
        await Tester.SignInAsUniqueAlice();
        var apiKey = await Tester.Commander.Call(new Accounts_CreateApiKey { Session = Tester.Session, Name = "k" });
        var client = await CreateMcpClient(apiKey);
        (await client.ListToolsAsync()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExpiredJwtShouldBeInvalidToken()
    {
        await Tester.SignInAsUniqueAlice();
        var (_, tokens) = await ConnectClient();
        await Task.Delay(TimeSpan.FromSeconds(4)); // fixture lifetime is 3s
        var response = await SendInitialize("Bearer " + tokens.GetProperty("access_token").GetString());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("error=\"invalid_token\"");
    }

    [Fact]
    public async Task RevokedGrantShouldRejectJwtAndRefresh()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (clientId, tokens) = await ConnectClient();
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        var grants = Tester.AppServices.GetRequiredService<IOAuthGrants>();
        var grant = (await grants.List(Tester.Session, default)).Single(g => g.ClientId == clientId);

        // act
        await Tester.Commander.Call(new OAuthGrants_Revoke { Session = Tester.Session, AuthorizationId = grant.Id });

        // assert
        await ComputedTest.When(async _ => (await SendInitialize("Bearer " + accessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized), TimeSpan.FromSeconds(10));
        var (status, body) = await Refresh(clientId, tokens.GetProperty("refresh_token").GetString()!);
        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task DeactivatedBackingSessionShouldRejectJwt()
    {
        await Tester.SignInAsUniqueAlice();
        var (_, tokens) = await ConnectClient();
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        var sid = ReadJwt(accessToken).Claims.Single(c => c.Type == "sid").Value;
        await Tester.Commander.Call(new Accounts_DeactivateSession { Session = Tester.Session, IdPrefix = sid[..CoreConstants.Session.IdPrefixLength] });
        await ComputedTest.When(async _ => (await SendInitialize("Bearer " + accessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task JwtForAnotherAudienceShouldBeRejected()
    {
        await Tester.SignInAsUniqueAlice();
        var clientId = await RegisterClient("https://c.example/cb");
        var pkce = NewPkce();
        var code = GetQueryValue((await Authorize(clientId, "https://c.example/cb", pkce, resource: "https://other.example/api")).Headers.Location!, "code");
        var (_, tokens) = await ExchangeCode(clientId, "https://c.example/cb", code, pkce.Verifier, "https://other.example/api");
        var response = await SendInitialize("Bearer " + tokens.GetProperty("access_token").GetString());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```
`CreateMcpClient(token)` and `SendInitialize(authorization)` go into `OAuthTestBase` — copy them from `McpTestBase.CreateClientWithRawKey` and `AuthTest.SendInitialize`. Replace the `...` in the first test with the same entry-reading approach `McpMessageToolsTest` uses after `post_message` (read that file first).

- [ ] **Step 2: Run** → JWT requests get 401 "Token is not an API key".

- [ ] **Step 3: Authenticator**

```csharp
using ActualChat.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace ActualChat.OAuth;

/// <summary>
/// Turns an OpenIddict-issued access token into the grant's backing <see cref="Session"/>:
/// signature/expiry via OpenIddict validation (in-process), then the session row — which is
/// also the revocation check. Reusable by any resource endpoint, not only MCP.
/// </summary>
public sealed class OAuthBearerAuthenticator(IServiceProvider services)
{
    private ISessionsBackend SessionsBackend { get; } = services.GetRequiredService<ISessionsBackend>();
    private UrlMapper UrlMapper { get; } = services.UrlMapper();

    public async Task<Session?> TryGetSession(HttpContext httpContext, string resourcePath, CancellationToken cancellationToken)
    {
        var result = await httpContext.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme).ConfigureAwait(false);
        if (!result.Succeeded || result.Principal is not { } principal)
            return null;

        var resource = UrlMapper.ToAbsolute(resourcePath);
        if (!principal.HasAudience(resource))
            return null;

        var session = SessionExt.NewValidOrNull(principal.GetClaim(OAuthConstants.SessionIdClaim));
        if (session is null || session.Kind != SessionKind.OAuth)
            return null;

        var info = await SessionsBackend.Get(session, cancellationToken).ConfigureAwait(false);
        if (info is null || !info.IsActive || info.UserId is not { IsGuest: false })
            return null;

        return session;
    }
}
```
(`HasAudience` is an OpenIddict principal extension; if absent, `principal.GetAudiences().Contains(resource)`.) Register as singleton in `OAuthModule`. Since the middleware runs after `UseAuthentication`, `AuthenticateAsync` with the validation scheme works from middleware.

- [ ] **Step 4: Middleware branch** (`McpAuthMiddleware.Invoke`)

```csharp
var token = TryGetBearer(httpContext);
if (token is null) {
    await Reject(httpContext, null, "Missing or malformed Authorization: Bearer header.").ConfigureAwait(false);
    return;
}

Session? session;
if (token.StartsWith(CoreConstants.Session.ApiKeyPrefix)) {
    session = SessionExt.NewValidOrNull(token);
    if (session is not null) {
        var info = await SessionsBackend.Get(session, httpContext.RequestAborted).ConfigureAwait(false);
        if (info is null || !info.IsActive || info.UserId is not { IsGuest: false })
            session = null;
    }
}
else
    session = await BearerAuthenticator.TryGetSession(httpContext, Settings.Route, httpContext.RequestAborted).ConfigureAwait(false);

if (session is null) {
    await Reject(httpContext, "invalid_token", "The token is invalid, expired, or revoked.").ConfigureAwait(false);
    return;
}

httpContext.Items[McpSessionAccessor.HttpContextItemKey] = session;
await next(httpContext).ConfigureAwait(false);
```
`TryGetBearer` is the old `TryGetSession` returning the raw token string. `BearerAuthenticator` resolved lazily: `private OAuthBearerAuthenticator BearerAuthenticator => field ??= Services.GetRequiredService<OAuthBearerAuthenticator>();` — it exists only when `OAuthModule` is enabled; if it's not registered, treat non-API-key tokens as invalid (`Services.GetService<>()` null → `session = null`). `Mcp.csproj` gets `<ProjectReference Include="..\OAuth.Service\OAuth.Service.csproj" />`. Existing `AuthTest.NonApiKeySession_IsRejected` (a browser session id as bearer) must still fail with 401 — a plain session id is not a JWT, so OpenIddict validation fails → null → 401. 

- [ ] **Step 5: Run** `McpAccessTest` + `Mcp.IntegrationTests` entirely → PASS.

- [ ] **Step 6: Commit** — `feat(mcp): accept OAuth access tokens next to API keys`

---
### Task 9: Client-side revocation and the pruner

**Files:**
- Create: `src/dotnet/OAuth.Service/Handlers/RevocationSessionHandler.cs`, `src/dotnet/OAuth.Service/OAuthPruner.cs`
- Modify: `src/dotnet/OAuth.Service/Module/OAuthModule.cs`
- Test: `tests/OAuth.IntegrationTests/RevocationTest.cs`, `PrunerTest.cs`

**Interfaces:**
- Produces: `POST /oauth/revoke` (`token`, `client_id`) deactivates the backing session once the authorization has no valid refresh token left; `OAuthPruner.RunOnce(CancellationToken)` (public, for tests) applies the spec's three rules.

- [ ] **Step 1: Failing tests**

```csharp
[Collection(nameof(OAuthCollection))]
public class RevocationTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task RevokingRefreshTokenShouldDeactivateSession()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (clientId, tokens) = await ConnectClient();
        var accessToken = tokens.GetProperty("access_token").GetString()!;

        // act
        var response = await Http.PostAsync("/oauth/revoke", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["token"] = tokens.GetProperty("refresh_token").GetString()!, ["client_id"] = clientId,
        }));

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await ComputedTest.When(async _ => (await SendInitialize("Bearer " + accessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized), TimeSpan.FromSeconds(10));
        var grants = await Tester.AppServices.GetRequiredService<IOAuthGrants>().List(Tester.Session, default);
        grants.Should().NotContain(g => g.ClientId == clientId);
    }
}

[Collection(nameof(OAuthCollection))]
public class PrunerTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task OrphanDcrClientsShouldBePrunedButGrantedOnesKept()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var orphan = await RegisterClient("https://o.example/cb");
        var (granted, _) = await ConnectClient();
        var pruner = Tester.AppServices.GetRequiredService<OAuthPruner>();
        using var scope = Tester.AppServices.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        await pruner.MarkRegisteredAt(orphan, DateTime.UtcNow - TimeSpan.FromDays(2), default); // test hook, see below

        // act
        await pruner.RunOnce(default);

        // assert
        (await applications.FindByClientIdAsync(orphan, default)).Should().BeNull();
        (await applications.FindByClientIdAsync(granted, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task AuthorizationWithDeadSessionShouldBeRevoked()
    {
        await Tester.SignInAsUniqueAlice();
        var (clientId, tokens) = await ConnectClient();
        var sid = ReadJwt(tokens.GetProperty("access_token").GetString()!).Claims.Single(c => c.Type == "sid").Value;
        await Tester.Commander.Call(new AccountsBackend_SignOut(new Session(sid), Deactivate: true));
        await Tester.AppServices.GetRequiredService<OAuthPruner>().RunOnce(default);
        var (status, _) = await Refresh(clientId, tokens.GetProperty("refresh_token").GetString()!);
        status.Should().Be(HttpStatusCode.BadRequest);
    }
}
```
`MarkRegisteredAt` is a small `internal` helper on `OAuthPruner` (assembly has `InternalsVisibleTo` for `*.IntegrationTests` via `Directory.Build.props` — `OAuth.Service` is `ProjectKind=Service`, so `ActualChat.OAuth.IntegrationTests` — check `RootNamespace`-based name matches the test assembly name, else add an explicit `InternalsVisibleTo`).

- [ ] **Step 2: Run** → revoke returns OK but the JWT still works; pruner type missing.

- [ ] **Step 3: Revocation handler**

```csharp
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace ActualChat.OAuth.Handlers;

// After OpenIddict revoked the token: if the authorization has no valid refresh token left,
// the client is gone for good — drop the grant and its session so the user's list stays honest.
public sealed class RevocationSessionHandler(IServiceProvider services) : IOpenIddictServerHandler<ApplyRevocationResponseContext>
{
    public static readonly OpenIddictServerHandlerDescriptor Descriptor
        = OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyRevocationResponseContext>()
            .UseScopedHandler<RevocationSessionHandler>()
            .SetOrder(int.MaxValue - 100_000)
            .Build();

    private IOpenIddictTokenManager Tokens { get; } = services.GetRequiredService<IOpenIddictTokenManager>();
    private IOpenIddictAuthorizationManager Authorizations { get; } = services.GetRequiredService<IOpenIddictAuthorizationManager>();
    private OAuthGrants Grants { get; } = services.GetRequiredService<OAuthGrants>();

    public async ValueTask HandleAsync(ApplyRevocationResponseContext context)
    {
        if (context.Response.Error is not null || context.Request?.Token is not { } reference)
            return;

        var cancellationToken = context.CancellationToken;
        var token = await Tokens.FindByReferenceIdAsync(reference, cancellationToken).ConfigureAwait(false);
        var authorizationId = token is null ? null : await Tokens.GetAuthorizationIdAsync(token, cancellationToken).ConfigureAwait(false);
        if (authorizationId is null)
            return;

        await foreach (var t in Tokens.FindByAuthorizationIdAsync(authorizationId, cancellationToken).ConfigureAwait(false)) {
            if (await Tokens.GetTypeAsync(t, cancellationToken).ConfigureAwait(false) == TokenTypeHints.RefreshToken
                && await Tokens.GetStatusAsync(t, cancellationToken).ConfigureAwait(false) == Statuses.Valid)
                return;
        }

        var authorization = await Authorizations.FindByIdAsync(authorizationId, cancellationToken).ConfigureAwait(false);
        if (authorization is not null)
            await Grants.Revoke(services, authorization, cancellationToken).ConfigureAwait(false);
    }
}
```
Register: `o.AddEventHandler(RevocationSessionHandler.Descriptor);` + `services.AddScoped<RevocationSessionHandler>();`. `OAuthGrants.Revoke` takes a scoped provider — the handler's `services` is the request scope. Reference refresh tokens are looked up by `FindByReferenceIdAsync`; if 7.7 hashes reference ids, use whatever `OpenIddictServerHandlers.Revocation` uses to resolve the token (search the XML doc for `ReferenceId`).

- [ ] **Step 4: Pruner**

```csharp
using ActualChat.OAuth.Module;
using ActualChat.Users;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.OAuth;

public sealed class OAuthPruner : WorkerBase
{
    private static readonly TimeSpan Period = TimeSpan.FromHours(1);

    private IServiceProvider Services { get; }
    private OAuthGrants Grants { get; }
    private ISessionsBackend SessionsBackend { get; }
    private OAuthSettings Settings { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public OAuthPruner(IServiceProvider services)
    {
        Services = services;
        Grants = services.GetRequiredService<OAuthGrants>();
        SessionsBackend = services.GetRequiredService<ISessionsBackend>();
        Settings = services.GetRequiredService<OAuthSettings>();
        Clocks = services.Clocks();
        Log = services.LogFor(GetType());
        this.Start();
    }

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(RunOnce)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(30, 600), Log)
            .AppendDelay(Period, Clocks.CpuClock)
            .CycleForever()
            .Run(cancellationToken);

    public async Task RunOnce(CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var now = Clocks.SystemClock.Now.ToDateTime();

        await tokens.PruneAsync(now - TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);

        var dcrCutoff = now - Settings.DcrPruneAge;
        await foreach (var application in applications.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false)) {
            var properties = await applications.GetPropertiesAsync(application, cancellationToken).ConfigureAwait(false);
            if (!properties.TryGetValue(OAuthConstants.Properties.RegisteredVia, out var via) || via.GetString() != OAuthConstants.RegisteredVia.Dcr)
                continue;
            if (!properties.TryGetValue(OAuthConstants.Properties.RegisteredAt, out var at) || at.GetDateTime() > dcrCutoff)
                continue;

            var id = (await applications.GetIdAsync(application, cancellationToken).ConfigureAwait(false))!;
            if (await authorizations.FindByApplicationIdAsync(id, cancellationToken).AnyAsync(cancellationToken).ConfigureAwait(false))
                continue;

            await applications.DeleteAsync(application, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Pruned orphan DCR client {ApplicationId}", id);
        }

        await foreach (var authorization in authorizations.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false)) {
            if (await authorizations.GetStatusAsync(authorization, cancellationToken).ConfigureAwait(false) != Statuses.Valid)
                continue;

            var sessionId = await Grants.GetSessionId(scope.ServiceProvider, authorization, cancellationToken).ConfigureAwait(false);
            var info = sessionId is null ? null : await SessionsBackend.Get(new Session(sessionId), cancellationToken).ConfigureAwait(false);
            if (info is { IsActive: true })
                continue;

            await Grants.Revoke(scope.ServiceProvider, authorization, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Revoked authorization with a dead session: {SessionId}", sessionId);
        }
    }

    internal async Task MarkRegisteredAt(string clientId, DateTime registeredAt, CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = (await applications.FindByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false))!;
        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, application, cancellationToken).ConfigureAwait(false);
        descriptor.Properties[OAuthConstants.Properties.RegisteredAt] = JsonSerializer.SerializeToElement(registeredAt);
        await applications.UpdateAsync(application, descriptor, cancellationToken).ConfigureAwait(false);
    }
}
```
Register `services.AddSingleton<OAuthPruner>()` and add it to the hosted services the way other `WorkerBase` singletons are started — check `ShardRoutingMonitor` registration in `ChatServiceModule` (`services.AddHostedService(c => c.GetRequiredService<…>())` or `AddSingleton<IHostedService>`), copy that. `AsyncChain.AppendDelay` — if the name differs, use `.Delay(Period)`/`.PrependDelay` per `ActualLab.Async.AsyncChainExt`. `ListAsync` on managers takes `(int? count, int? offset, CancellationToken)`.

- [ ] **Step 5: Run** both tests → PASS.

- [ ] **Step 6: Commit** — `feat(oauth): client-side revocation drops the grant; hourly pruner`

---

### Task 10: Consent page

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Pages/OAuthConsentPage.razor`, `oauth-consent-page.css` (register the CSS the way `user-page.css` is — see `docs/ui/components.md`)
- Modify: `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs` (`public IOAuthGrants OAuthGrants => field ??= Services.GetRequiredService<IOAuthGrants>();`)
- Modify: `src/dotnet/Localization/Resources/Strings.en.json` + 18 catalogs, `LocalizedStringsLocalizerExt.cs`
- Test: manual (`docs/oauth.md` checklist); `AppLocalizationTest` guards the strings

**Interfaces:**
- Consumes: `IOAuthGrants.GetClient`, `OAuthGrants_Approve`, `AccountUI.RequestSignInFromHomePage`, `History.ForceReload`.

- [ ] **Step 1: Strings** (English; group comment `// OAuth consent page`):

| Key | English |
|---|---|
| `OAuthConsent_Title` | Connect an app |
| `OAuthConsent_Heading_Format` | **{0}** wants to access your Voxt account |
| `OAuthConsent_RedirectHost_Format` | You'll be sent back to {0} |
| `OAuthConsent_Loopback` | This app runs on your computer |
| `OAuthConsent_Scope_Mcp` | Read and post in your chats |
| `OAuthConsent_Scope_OfflineAccess` | Stay connected without asking again |
| `OAuthConsent_SignedInAs_Format` | Signed in as {0} |
| `OAuthConsent_Approve` | Allow |
| `OAuthConsent_Deny` | Deny |
| `OAuthConsent_UnknownClient` | This app isn't registered with Voxt. Check the link you followed. |
| `OAuthConsent_Error_Format` | The request couldn't be processed: {0} |
| `SignIn_ToAuthorizeApp` | Sign in to connect this app |

(`_Format` keys with bold: emit plain `{0}` and bold the client name in markup instead — the catalog holds prose only.) Add the typed members; run the derive scripts.

- [ ] **Step 2: Page**

```razor
@page "/oauth/consent"
@using ActualChat.OAuth
@inherits ComponentBase<AppUIHub>

<MainHeader>@L.OAuthConsent_Title</MainHeader>

@if (_signInRequested) {
    return;
}

<div class="oauth-consent-page">
    @if (!_error.IsNullOrEmpty()) {
        <Tile><TileItem><Content>@L.OAuthConsent_Error_Format(_error)</Content></TileItem></Tile>
    } else if (_client is null) {
        <Tile><TileItem><Content>@L.OAuthConsent_UnknownClient</Content></TileItem></Tile>
    } else {
        <Tile>
            <TileItem>
                <Content>
                    <div class="c-heading">@L.OAuthConsent_Heading_Format(_client.ClientName)</div>
                    <div class="c-redirect">@L.OAuthConsent_RedirectHost_Format(string.Join(", ", _client.RedirectHosts))</div>
                    @if (_client.IsLoopback) {
                        <div class="c-loopback">@L.OAuthConsent_Loopback</div>
                    }
                </Content>
            </TileItem>
        </Tile>
        <TileTopic Topic="@L.OAuthConsent_SignedInAs_Format(_account?.Avatar.Name ?? "")"/>
        <Tile>
            @foreach (var scope in _scopes) {
                <TileItem IsHoverable="false"><Content>@ScopeText(scope)</Content></TileItem>
            }
        </Tile>
        <div class="c-buttons">
            <Button Class="btn-primary" Click="@OnApprove" IsDisabled="@_isBusy">@L.OAuthConsent_Approve</Button>
            <Button Class="btn-outline" Click="@OnDeny" IsDisabled="@_isBusy">@L.OAuthConsent_Deny</Button>
        </div>
    }
</div>

@code {
    private bool _signInRequested;
    private bool _isBusy;
    private string? _error;
    private AccountFull? _account;
    private OAuthClientInfo? _client;
    private string[] _scopes = [];
    private string _query = "";

    private IOAuthGrants OAuthGrants => Hub.OAuthGrants;

    protected override async Task OnParametersSetAsync() {
        var uri = new Uri(Nav.Uri);
        _query = uri.Query;
        var q = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        if (q.TryGetValue("error", out var error)) {
            _error = q.TryGetValue("error_description", out var d) ? d.ToString() : error.ToString();
            return;
        }

        _account = await AccountUI.OwnAccount.Use();
        if (_account.IsGuest || !_account.IsActive()) {
            _signInRequested = true;
            _ = AccountUI.RequestSignInFromHomePage(L.SignIn_ToAuthorizeApp, History.LocalUrl);
            return;
        }

        var clientId = q.TryGetValue("client_id", out var c) ? c.ToString() : "";
        _scopes = (q.TryGetValue("scope", out var s) ? s.ToString() : "mcp").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _client = clientId.IsNullOrEmpty() ? null : await OAuthGrants.GetClient(Session, clientId, default);
    }

    private string ScopeText(string scope)
        => scope switch {
            "mcp" => L.OAuthConsent_Scope_Mcp,
            "offline_access" => L.OAuthConsent_Scope_OfflineAccess,
            _ => scope,
        };

    private async Task OnApprove() {
        _isBusy = true;
        var command = new OAuthGrants_Approve { Session = Session, ClientId = _client!.ClientId, Scopes = _scopes.ToApiArray() };
        var (_, error) = await UICommander.Run(command);
        if (error is not null) {
            _isBusy = false;
            return;
        }

        await History.ForceReload("OAuth consent approved", "/oauth/authorize" + _query);
    }

    private Task OnDeny()
        => History.ForceReload("OAuth consent denied", "/oauth/authorize" + _query + "&voxt_deny=1").AsTask();
}
```
Check: `Nav` (NavigationManager) accessor name on `ComponentBase<AppUIHub>` (`History.Uri`/`Hub.Nav` — read `ChatInvitePage`/`UserPage` for what's available), `UICommander.Run` return shape, `Button` component parameter names (`IsDisabled`, `Click`), `TileTopic`. CSS: `.oauth-consent-page` with `@apply flex-y gap-4 p-4 max-w-md mx-auto`, `.c-heading` `font-medium text-lg`, `.c-redirect`/`.c-loopback` `text-sm text-03`, `.c-buttons` `flex-x gap-2 justify-end`. Blazor routes: confirm `/oauth/consent` isn't shadowed by MVC (controllers map `/oauth/authorize|token|register|revoke` only) and that the Blazor router gets unknown `/oauth/*` paths (it does — `MapRazorComponents` is the fallback).

- [ ] **Step 3: Verify manually** — run the server (watch mode per CLAUDE.md), open `https://local.voxt.ai/oauth/consent?client_id=nope&scope=mcp` → "isn't registered" tile; then follow the Task 12 Claude Code checklist item once Task 12 lands. Run `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~AppLocalizationTest"` → PASS.

- [ ] **Step 4: Commit** — `feat(ui): OAuth consent page`

---
### Task 11: "API & apps" settings — Connected apps section

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/Settings/ConnectedAppsSettings.razor`, `ApiAndAppsSettings.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/Settings/SettingsModal.razor:76-79`, `settings-modal.css`
- Modify: `Strings.*.json` (19 hand-written) + `LocalizedStringsLocalizerExt.cs`

**Interfaces:**
- Consumes: `IOAuthGrants.List`, `OAuthGrants_Revoke`.

- [ ] **Step 1: Strings** — new keys: `Settings_ApiAndApps` = "API & apps"; `ConnectedApps_Title` = "Connected apps"; `ConnectedApps_Empty` = "Apps you authorize via OAuth (Claude, Cursor, …) appear here."; `ConnectedApps_Connected_Format` = "Connected {0}"; `ConnectedApps_LastUsed_Format` = "Last used {0}"; `ConnectedApps_Revoke` = "Revoke access"; `ConnectedApps_RevokeConfirm_Format` = "Revoke {0}'s access to your account? The app will have to ask again."; `ConnectedApps_RevokeTitle` = "Revoke access". Keep `Settings_ApiKeys` (still used as the section topic — check other usages before renaming). Derive scripts + typed members.

- [ ] **Step 2: Components**

`ConnectedAppsSettings.razor`:
```razor
@using ActualChat.OAuth
@inherits ComputedStateComponent<AppUIHub, ConnectedAppsSettings.Model>
@{
    var m = State.Value;
}

<TileTopic Topic="@L.ConnectedApps_Title"/>

@if (m.Grants.Count == 0) {
    <Tile><TileItem IsHoverable="false"><Content><span class="c-empty">@L.ConnectedApps_Empty</span></Content></TileItem></Tile>
} else {
    <Tile Class="api-key-tile">
        @foreach (var grant in m.Grants) {
            <TileItem IsHoverable="false">
                <Content>
                    <div class="c-key-info">
                        <span class="c-key-name">@grant.ClientName</span>
                    </div>
                    <div class="c-deactivate">
                        <ButtonRound Class="btn-sm btn-danger" Tooltip="@L.ConnectedApps_Revoke" Click="@(() => OnRevokeClick(grant))">
                            <i class="icon-trash03 text-xl"></i>
                        </ButtonRound>
                    </div>
                </Content>
                <Caption>
                    <span class="c-key-expires">@L.ConnectedApps_Connected_Format(FormatDate(grant.CreatedAt)) · @L.ConnectedApps_LastUsed_Format(FormatDate(grant.LastUsedAt))</span>
                </Caption>
            </TileItem>
            @if (grant != m.Grants[^1]) {
                <Divider />
            }
        }
    </Tile>
}

@code {
    private IOAuthGrants OAuthGrants => Hub.OAuthGrants;

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() { InitialValue = new Model(), Category = GetStateCategory(GetType()) };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken)
        => new() { Grants = await OAuthGrants.List(Session, cancellationToken).ConfigureAwait(false) };

    private string FormatDate(Moment moment)
        => DateTimeConverter.ToLocalTime(moment.ToDateTime()).ToString("g", DateFormatter);

    private async Task OnRevokeClick(OAuthGrant grant) {
        var confirmed = false;
        var model = new ConfirmModal.Model(false, L.ConnectedApps_RevokeConfirm_Format(grant.ClientName), () => { confirmed = true; }) {
            Title = L.ConnectedApps_RevokeTitle,
            ConfirmButtonText = L.Common_Yes,
        };
        var modalRef = await ModalUI.Show(model).ConfigureAwait(false);
        await modalRef.WhenClosed.ConfigureAwait(false);
        if (!confirmed)
            return;

        await UICommander.Run(new OAuthGrants_Revoke { Session = Session, AuthorizationId = grant.Id }).ConfigureAwait(false);
    }

    // Nested types

    public sealed record Model
    {
        public ApiArray<OAuthGrant> Grants { get; init; } = [];
    }
}
```
(`DateTimeConverter`/`DateFormatter` are what `ApiKeySettings` uses — copy its exact accessors.)

`ApiAndAppsSettings.razor`:
```razor
@inherits ComponentBase<AppUIHub>

<TileTopic Topic="@L.Settings_ApiKeys"/>
<ApiKeySettings/>
<ConnectedAppsSettings/>
```
(If `ApiKeySettings` already opens with its own topic, drop the first line.) `SettingsModal.razor`: label `L.Settings_ApiAndApps`, `Content = @<ApiAndAppsSettings/>`. CSS: `.api-key-tile .c-empty { @apply text-sm text-03; }`.

- [ ] **Step 3: Verify** — `npm run build:Verify` isn't needed (no TS); build App.Server; open Settings → API & apps on local; after a Task 12 connection the app appears; Revoke removes it and the client's next call fails. `AppLocalizationTest` → PASS.

- [ ] **Step 4: Commit** — `feat(ui): Connected apps section in the API & apps settings tab`

---

### Task 12: SDK end-to-end test

**Files:**
- Create: `tests/OAuth.IntegrationTests/McpOAuthClientTest.cs`

**Interfaces:**
- Consumes: `ModelContextProtocol.Client.ClientOAuthOptions` (`HttpClientTransportOptions.OAuth`), `DynamicClientRegistrationOptions`, `AuthorizationRedirectDelegate`.

- [ ] **Step 1: Test**

```csharp
[Collection(nameof(OAuthCollection))]
public class McpOAuthClientTest(OAuthCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : OAuthTestBase<OAuthCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task SdkClientShouldRegisterConsentCallAndRefresh()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true, title: "SDK chat");
        var redirectUri = new Uri("http://localhost/callback");
        var transport = new HttpClientTransport(new HttpClientTransportOptions {
            Endpoint = new Uri(BaseUri, "/api/mcp"),
            OAuth = new ClientOAuthOptions {
                ClientName = "SDK Test",
                RedirectUri = redirectUri,
                Scopes = ["mcp", "offline_access"],
                DynamicClientRegistration = new DynamicClientRegistrationOptions { ClientName = "SDK Test" },
                AuthorizationRedirectDelegate = DriveConsent,
            },
        });

        // act
        var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();
        var post = await client.CallToolAsync("post_message", new Dictionary<string, object?> { ["chatId"] = chatId.Value, ["text"] = "hello from sdk" });
        await Task.Delay(TimeSpan.FromSeconds(4)); // access token (3s) expires → SDK refreshes on the 401
        var list = await client.CallToolAsync("list_messages", new Dictionary<string, object?> { ["chatId"] = chatId.Value, ["limit"] = 10 });

        // assert
        tools.Select(t => t.Name).Should().Contain("post_message");
        post.IsError.Should().NotBe(true);
        list.IsError.Should().NotBe(true, because: "the SDK must have refreshed the expired access token transparently");
        list.StructuredContent.ToString().Should().Contain("hello from sdk");
    }

    [Fact]
    public async Task TwoUsersShouldGetIsolatedGrants()
    {
        // Same shape as MultiUserAccessTest in Mcp.IntegrationTests, but each user connects an SDK client via OAuth:
        // Alice's client lists her private chat; Bob's client, same DCR client_id, must not see it.
        // Use a second WebClientTester (fixture.AppHost.NewWebClientTester(Out)) signed in as Bob and a
        // DriveConsent bound to that tester's session.
    }

    // Plays the browser: GET the authorize URL with the user's cookie, approve through the command, return the code.
    private async Task<string?> DriveConsent(Uri authorizationUri, Uri redirectUri, CancellationToken cancellationToken)
    {
        var relative = authorizationUri.PathAndQuery;
        var first = await SendAsUser(HttpMethod.Get, relative);
        if (first.Headers.Location?.ToString().Contains("/oauth/consent") == true) {
            var q = QueryHelpers.ParseQuery(authorizationUri.Query);
            await Tester.Commander.Call(new OAuthGrants_Approve {
                Session = Tester.Session, ClientId = q["client_id"].ToString(), Scopes = q["scope"].ToString().Split(' ').ToApiArray(),
            }, cancellationToken);
            first = await SendAsUser(HttpMethod.Get, relative);
        }
        first.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return GetQueryValue(first.Headers.Location!, "code");
    }
}
```
Fill in `TwoUsersShouldGetIsolatedGrants` fully (no comment-only body): create Bob's tester, a private chat for Alice, two SDK clients, assert `list_group_chats` from Bob's client lacks Alice's chat and `list_messages` on it returns an error. The `AuthorizationRedirectDelegate` signature and `DynamicClientRegistrationOptions` property names must be read from `ModelContextProtocol.Core.xml` in the NuGet cache before writing the final code.

- [ ] **Step 2: Run** `McpOAuthClientTest` → PASS. Then the **entire** `OAuth.IntegrationTests` and `Mcp.IntegrationTests` projects → PASS.

- [ ] **Step 3: Commit** — `test(oauth): MCP SDK end-to-end OAuth flow`

---

### Task 13: Living doc, config, acceptance checklist

**Files:**
- Create: `docs/oauth.md`; add it to `docs/.vitepress/config.mts` sidebar and `docs/index.md` (see how `docs/passkeys.md` was added on `feat/passkeys` for the exact spots)
- Modify: `src/dotnet/App.Server/appsettings.Development.json` (`"OAuthSettings": { "Route": "/oauth" }` if a section is needed for discoverability — mirror how `McpSettings` is (not) present), ops notes for prod secret `OAuthSettings__SigningCertificateBase64`
- Regenerate: `docs/api-index*.md` if a script exists for it (check `docs/AGENTS.md`)
- Delete: `docs/superpowers/specs/2026-09-15-oauth-for-mcp-design.md` and this plan **only when** the branch is prepared for merge (`/prepare-merge` does it)

- [ ] **Step 1: Write `docs/oauth.md`** covering: purpose; the two token kinds and the shared principal (`SessionKind.OAuth`); endpoints table; DCR vs CIMD; consent flow; revocation paths; settings (`OAuthSettings`) and how to mint the prod certificate (`openssl req -x509 -newkey rsa:2048 -days 1095 -subj "/CN=voxt-oauth" -keyout k.pem -out c.pem -nodes && openssl pkcs12 -export -in c.pem -inkey k.pem -out oauth.pfx -passout pass:… && base64 -w0 oauth.pfx`), key rotation; the pruner; observability (log lines to grep: `DCR:`, `CIMD:`, `Authorize:`, `Pruned`, `Revoked`); troubleshooting ("Couldn't reach the MCP server" = discovery; `invalid_client` = CIMD fetch; loopback port); and the **acceptance checklist**:

```
1. Claude Code vs local:  NODE_EXTRA_CA_CERTS=.config/local.voxt.ai/ssl/local.voxt.ai.crt \
     claude mcp add --transport http voxt https://local.voxt.ai/api/mcp
   → /mcp → Authenticate → browser opens /oauth/consent → Allow → "Connected"; run list_places; then
   Settings → API & apps shows "Claude Code"; Revoke → next tool call re-prompts.
2. Claude Code vs dev (same, https://dev.voxt.ai/api/mcp).
3. claude.ai / Cowork custom connector vs dev: "Use Claude's published identity" (CIMD); then remove and
   re-add with "Register automatically" (DCR). Post a message from a Cowork task.
4. Cursor vs dev (pure DCR).
5. Revoke from Connected apps while a client is connected → the client re-prompts on its next call.
```

- [ ] **Step 2: Style hook + full build** — `dotnet build src/dotnet/App.Server/App.Server.csproj` warning-free for new files; run `dotnet test tests/OAuth.IntegrationTests`, `tests/Mcp.IntegrationTests`, `tests/Users.IntegrationTests --filter Session`, `tests/Chat.UI.Blazor.UnitTests --filter AppLocalizationTest` → all PASS.

- [ ] **Step 3: Commit** — `docs(oauth): living doc, prod configuration and acceptance checklist`

- [ ] **Step 4: `/track-issue in-progress`** — the branch now has code, so #4539 moves to In Progress (the skill never moves it backward).

---

## Self-review notes

- Spec coverage: §Architecture → Tasks 2-9; §Data model → 1, 5; §Endpoints → 2, 3, 4, 6, 7, 9; §Consent/Settings → 10, 11; §Errors/security → 3 (401 shape), 4 (DCR validation, rate limit), 6 (PKCE/redirect), 7 (CIMD caps), 8 (`aud`, session check), 9 (pruning); §Tests layer 1 → 2-9, layer 2 → 12, layer 3 → 13. Metrics counters from §Observability are intentionally reduced to structured log lines (no counter plumbing exists for auth today; add when there is a dashboard to feed).
- Type consistency: `OAuthGrants.FindAuthorization/GetSessionId/TouchSession/Revoke` signatures are used identically in Tasks 6, 9; `OAuthConstants` keys used in 5, 7, 9; test helpers (`RegisterClient`, `Authorize`, `ConnectClient`, `SendAsUser`, `SendInitialize`, `CreateMcpClient`, `ReadJwt`, `Refresh`) are defined in Tasks 4, 6, 8 and consumed afterwards.
- Known verification points (not placeholders — exact names to confirm against the 7.7 XML docs at implementation time, each has a fallback in its task): `ValidateClientId` descriptor name, `Permissions.Endpoints.Revocation`, `context.TokenEndpointAuthenticationMethods`, `OpenIddictApplicationManager<T>` ctor, `FindByReferenceIdAsync`, `AsyncChain` delay operator name, MCP SDK OAuth option property names.
