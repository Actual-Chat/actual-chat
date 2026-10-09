# Voxt Management UI (Mui) Implementation Plan

**Goal:** A separate, web-only front-end for staff (the Voxt Management UI, `Mui`) under `/m/` ("manage"), built on MudBlazor and Blazor Server, that takes over the staff pages now living in the app's `/test/*` pages: flow monitoring, system commands, mesh info, the chat-to-place copy tool, and the digest preview. It shares no UI code, JS, or CSS with the app UI.

**Architecture:** A new Razor class library `Mui` is referenced by `App.Server`, which maps it at `/m/` with its own HTML shell and the interactive Server render mode only. Components inject contract interfaces (`IDiagnostics`, `ISystemProperties`, `IAccounts`, commands via `ICommander`) and never server-only types, so adding a WASM render mode later does not require rewriting them. Access is limited to admin accounts through the existing session cookie.

**Tech Stack:** C# 15, Blazor Server, MudBlazor, Fusion compute state, xUnit.

## Scope

**In (phase 1: move what exists):**

| Source page | Mui page | Notes |
|---|---|---|
| `FlowsTestPage` (`/test/flows`) | Flows | Stats per flow type, instance list with filters, expandable console/error per instance |
| `SystemTestPage` (`/test/system`) | System | Host info, invalidate everything, prune computed graph, GC |
| `MeshTestPage` (`/test/mesh`) | Mesh | Nodes, mesh RPC refs, RPC peers, copyable report |
| `AdminCopyChatToPlacePage` (`/test/copy-chat2place`) | Tools | Copy chat to place, publish copied chat |
| `DigestTestPage` (`/test/digest`) | Digest | Digest email preview for the viewer's unread chats or selected chats; rendered email and per-chat diagnostics |

`SystemTestPage` actions that exist only to test the app UI ("Reload UI", "Throw Exception") are not moved. "Invalidate everything → on this front-end / locally" keep their current semantics; under Blazor Server "locally" is the whole server process, same as the existing guard in `SystemTestPage` says.

**Out (later, not designed here):** AI/Baska query console with personal-data filtering, metrics and growth dashboards, queue-length reporting, flow resume (single and by category), host/shard map, a WASM render mode, `m.voxt.ai` as a separate origin. The structure below leaves room for each; none is built now.

**Out of scope on purpose:** no embedding into Voxt apps, no MAUI, no localization (see Decisions).

## Decisions

1. **Blazor Server + MudBlazor**, as agreed. A TypeScript SPA was the alternative; it would need every page rewritten rather than ported.
2. **Same origin, `/m/` prefix**, not `m.voxt.ai`. The Fusion session cookie, `/signIn`, and ingress routing already work for the main host; a second origin would need cookie-domain and CORS work for no gain now. The shell reads the path base from one setting, so moving to a subdomain stays cheap.
3. **Hosted in the `App.Server` process**, not a new executable. The Mui pages call `IDiagnostics` and the system commands, which are server-side services; in-process, they run without extra RPC hops, and the mesh fan-out in `Diagnostics.GetMeshDiagInfo` keeps working as is.
4. **Isolated shell and bundle.** The Mui root page renders its own `<html>`, loads only MudBlazor's static assets and `blazor.web.js`. It does not load the app bundle, Tailwind CSS, or app TypeScript.
5. **English-only UI.** `docs/CODING_STYLE.md` requires every user-visible string to come from the localization catalog. The Management UI is for staff, and the pages it replaces already hardcode English. `docs/CODING_STYLE.md` and `docs/i18n.md` now list the Management UI (`/m/`) with the test pages as English-only developer surfaces.
6. **Feature folders registered through one nav registry.** Each feature (`Flows`, `System`, `Mesh`, `Tools`) is a folder with its page(s) and a single `MuiNavEntry` registration. A new section is a new folder plus one registration line.
7. **Migrated pages are removed from the app UI** once the admin versions are verified, so there is one place for each tool.

## Reuse

### Existing abstractions to reuse

- `IDiagnostics` / `Diagnostics` (`Api.Contracts/Chat/IDiagnostics.cs`, `Chat.Service/Diagnostics.cs`): mesh info, flow stats, flow list, flow details. Already admin-checked server-side through `RequireAdmin`; the UI check is only for navigation.
- `ISystemProperties` and the `SystemProperties_InvalidateEverything` / `SystemProperties_PruneComputedGraph` commands (`Api.Contracts/Users/ISystemProperties.cs`).
- `IAccounts.GetOwn(Session)` and `AccountFull.MustBeAdmin` for the access gate, the same check `RequireAccount MustBeAdmin` makes.
- `ComputedStateComponent<T>` from `ActualLab.Fusion.Blazor` for live state (`GetFlowDetails`, `GetMeshDiagInfo` are compute methods), instead of `AppUIHub`-based components.
- `ISessionResolver`, `AuthHelper.UpdateAuthState` and the session cookie handling from `RootServerPage.razor` (`Initialize()`): the Mui root reuses the same session establishment.
- `HostId`, `HostInfo`, `ComputedRegistry`, `ReloadUI`-free parts of `SystemTestPage`.
- `CopyChatToPlaceUI.CopyChat` / `PublishCopiedChat` and `ChatsBackend.CopyChat`: logic is reused; the UI-hub-bound part is replaced (task 7).
- `docs/ui/components.md` conventions for file layout and CSS class naming (`c-` prefix), applied inside `Mui`.

Searched `docs/api-index.md` and `api-index-full.md` for admin, diagnostics, dashboard, table and grid helpers; nothing reusable beyond the list above. `UI.Blazor` components (`Button`, `Form`, `MainHeader`, `CopyTrigger`, `RequireAccount`) are deliberately not reused: they depend on the app's styling, `AppUIHub`, and bundle, which this project isolates.

### Reusability of new components

| New component | Placement | Why |
|---|---|---|
| `Mui` (pages, layout, theme, nav registry) | New project `Mui` | Staff-only; keeping it out of `UI.Blazor*` is the point of the work |
| `MuiComponent`, `MuiComputedComponent<T>` (session, services, error reporting) | `Mui` | Depend on MudBlazor; useful only to Mui pages |
| Flow-status/diagnostic view models (formatting, status → severity) | `Mui`, plain classes with no MudBlazor types | Unit-testable now; movable to `ActualChat.Core` if another host needs them |
| Admin-only data the pages need that doesn't exist yet (see task 7) | `Api.Contracts` + service project (`IAdmin…` frontend service) | Contracts are what keep the pages WASM-ready |

No TypeScript is added.

## Global constraints

- `Mui` references contract and API projects only, never `*.Service` projects, `App.Server`, or `UI.Blazor*`. This is what keeps a later WASM render mode possible, and a build-time reference check enforces it (task 1).
- Admin access is enforced by the services (`RequireAdmin`), not only by the UI gate (`MuiGate`). New admin endpoints/services must call it.
- `test-…@actual.chat` accounts never become admin (existing rule); the plan does not touch how admin status is granted.
- Follow `docs/CODING_STYLE.md` (no `Async` suffix, no XML docs, line length); read `docs/ui/components.md` before the first component.

## Tasks

Each task is one commit. Tasks 1–2 are a gate: if the hosting spike (task 1) fails, stop and revisit decision 3 before building pages.

### Task 1: `Mui` project and hosting spike

**Files:**
- Create: `src/dotnet/Mui/Mui.csproj` (Razor SDK, `MudBlazor` package, project references to `Api`, `Api.Contracts` only)
- Create: `src/dotnet/Mui/MuiApp.razor` (the interactive component: MudBlazor providers, `Router`, session from the token), `Layout/MuiLayout.razor`, `Pages/HomePage.razor`, `_Imports.razor`
- Create: `src/dotnet/App.Server/Components/Pages/MuiRootPage.razor` (HTML shell and session establishment; it lives in `App.Server` because `AuthHelper` is in `Users.Service`, the same split as `RootServerPage` → `WebApp`)
- Modify: `Directory.Packages.props` (add `MudBlazor`), `src/dotnet/App.Server/App.Server.csproj`, `src/dotnet/App.Server/Module/AppServerModule.cs`, `ActualChat.sln`, `ActualChat.CI.slnf`
- Modify: `src/dotnet/App.Server/Module/ContentSecurityPolicy.cs` if MudBlazor's assets or inline styles are blocked

**What to prove:** `GET /m/` returns the Mui shell, interactive Server circuit connects, MudBlazor renders, and the app root at `/` still works unchanged.

**Hosting options, in the order to try:**

1. A second `app.MapRazorComponents<MuiRootPage>()`. **Tried and rejected:** both mappings add `/_blazor/initializers` and the circuit endpoints, so those requests fail with `AmbiguousMatchException` and the main app breaks.
2. **Used:** `Mui` is an additional assembly of the existing mapping, and `RootServerPage` renders `MuiRootPage` when the path starts with `/m`. The app's router does not include the Mui assembly. Mui pages are `@page "/m/..."`.

**Notes from reading the code:**
- `<base href>` stays `/`, as in the app shell. Blazor's router matches page templates against the path relative to the base, so a base of `/m/` would make the templates `/…` and break the endpoint mapping.
- MudBlazor's default Roboto font is loaded from Google Fonts, which the content security policy blocks. The theme uses a system font stack, and MudBlazor icons are inline SVG, so the policy needs no change.
- The service worker (`service-worker.ts`, scope `/`) handles push notifications and has no `fetch` handler, so it does not intercept `/m/` navigations.
- The circuit gets the session the same way the app does: the root page issues a `SecureToken` for the session, and the component decrypts it with `ISecureTokensBackend`. This is server-only; a WASM mode would pass the session hash instead.

Record which option works in `docs/ui/mui.md`.

**Verification:** `dotnet build ActualChat.CI.slnf`; a script (or MSBuild target) that fails if `Mui` references a `*.Service`, `App.*`, or `UI.Blazor*` project; manual: `/m/` loads, `/` loads, hot reload unaffected. (The `HostInfo.IsTested` guard skips Razor mapping in tests, so there is no integration test for this; the manual check is the gate.)

- [x] Create project, add shell and a placeholder page
- [x] Wire hosting (option 2: one root; option 1 failed at runtime)
- [x] Add the project-reference guard (an MSBuild target in `Mui.csproj`; checked by adding a `UI.Blazor` reference, which fails the build)
- [ ] Confirm `/` and `/m/` both serve

### Task 2: Session, admin gate, and base components

**Files:**
- Session establishment is already in `MuiRootPage` / `MuiApp` (task 1); this task adds the admin check on top of it
- Create: `src/dotnet/Mui/Components/MuiComputedComponent.cs`, `MuiGate.razor` (shows the sign-in hint and the non-admin message inline, no separate page)
- Create: `src/dotnet/Mui/Services/MuiAccess.cs` (decision logic), `MuiActions.cs` (runs commands, reports failures in a snackbar)
- Create: `tests/Mui.UnitTests/`

**Behavior:**
- Signed-out visitor → a hint with a link to the main site; sign in there, then open `/m/` again (same cookie). A direct provider link with a return URL can come later.
- Signed-in non-admin → a 403-style page that shows nothing about the admin features.
- Admin → layout renders.

`MuiComputedComponent<T>` is `ComputedStateComponent<T>` plus the injected `MuiActions`; `Session` already comes from Fusion's `CircuitHubComponentBase`.

**Verification:** unit tests for the gate's decision logic (account null / not admin / admin), kept as a plain class; manual check with an admin and a non-admin account.

- [x] Session establishment shared with the app's approach (task 1)
- [x] `MuiGate` gate
- [x] Base components

### Task 3: Layout, theme, navigation registry

**Files:**
- Create: `src/dotnet/Mui/Layout/MuiLayout.razor`, `NavMenu.razor`
- Create: `src/dotnet/Mui/Theme/MuiTheme.cs` (the `MudTheme`)
- Create: `src/dotnet/Mui/Navigation/MuiNavEntry.cs`, registration extension

**Details:**
- `MudTheme` palette uses Voxt's primary/accent colors (take the values from the app's design tokens, `tailwind.config.js` / `src/dotnet/UI.Blazor.App/styles.css`; copy the values, don't import the files). Light palette only (see the review changes below). Beyond that, MudBlazor defaults.
- `MuiNavEntry(Title, Href, Icon, Order)` registered in DI; `NavMenu` renders the sorted list. Features register themselves in their own `AddXxx` extension.

**Verification:** build; visual check of the layout.

- [x] Layout with app bar and drawer
- [x] Theme (Voxt blue, system font stack; values copied from `src/nodejs/styles/colors.css`)
- [x] Nav registry; only "Overview" is registered now, each feature task registers its own entry

### Task 4: System page

**Files:** `src/dotnet/Mui/Features/System/SystemPage.razor` (+ `.cs`)

Ports `SystemTestPage`: host id, kind, roles; invalidate everything (locally / on this front-end / everywhere); prune computed graph (same three); GC ×3. Destructive actions ask for confirmation through `IDialogService` (the old page had none). Drops "Reload UI" and "Throw Exception".

**Verification:** manual: each action runs and reports success or failure in a snackbar; "locally" is blocked on a server host with the same message as today.

- [x] Page and actions ("Invalidate everything: locally" is left out: under Blazor Server it is always blocked)
- [x] Confirmation dialogs (`IDialogService.ShowMessageBoxAsync`)

### Task 5: Mesh page

**Files:** `src/dotnet/Mui/Features/Mesh/MeshPage.razor`, `MeshNodeView.razor`

Ports `MeshTestPage` using `GetMeshDiagInfo` through `MuiComputedComponent<MeshDiagInfo?>`: a card per node (this node, then `Others`) with nodes table (state, roles, endpoint), mesh RPC refs table, RPC peers table with collapsible connection state (opened when not Connected). Refresh generates a new tag as today. "Copy report" produces the same text report as the old page, generated from the model rather than scraped from `innerText`.

**Verification:** manual against a multi-node setup if available, otherwise single node; unit test for report text generation.

- [x] Tables and per-peer expansion panels (open when a remote peer is not connected)
- [x] Refresh and report copy (`MeshReport` builds the text; clipboard through `navigator.clipboard`)

### Task 6: Flows page

**Files:** `src/dotnet/Mui/Features/Flows/FlowsPage.razor`, `FlowDetailsPanel.razor`, `FlowStatusChip.razor`

Ports `FlowsTestPage`:
- `MudDataGrid` for per-type stats (Total / Completed / Scheduled / Idle+Stuck / Failed); row click filters instances; failed and stuck counts highlighted.
- Instances grid with "Problematic only" and "Hide completed"; 200-row limit as today, shown in the footer; stale-response guard (generation counter) kept.
- Row expansion shows the console log and error through `GetFlowDetails` as live state for expanded rows only (as the page does now).
- Deep-linkable state: selected type and filters in the query string, so a link to a problem is shareable.

**Verification:** manual against dev data; unit test for the status-to-severity mapping and filter-to-query mapping.

- [x] Stats grid
- [x] Instances grid, filters, expansion (each expanded row hosts a `FlowDetailsPanel` with its own live state)
- [x] Query-string state (`type`, `problematic`, `completed`)

### Task 7: Tools page (copy chat to place)

**Investigation first:** `CopyChatToPlaceUI.CopyChat` / `PublishCopiedChat` are in the app UI project and take `AppUIHub`. Check what they do (command calls, confirmation, progress). If they only run commands, call the same commands from the admin page through `ICommander`. If they depend on app-UI services, add an admin-only frontend service (`IAdminChats` in `Api.Contracts`, implementation in `Chat.Service`, `RequireAdmin` inside) and make both callers use it.

**Files:** `src/dotnet/Mui/Features/Tools/CopyChatToPlacePage.razor`; possibly `Api.Contracts/Chat/IAdminChats.cs` and `Chat.Service/AdminChats.cs`

Form with chat id and place id, validated through `MudForm` and the id parsers (`ChatId.Parse`, `PlaceId.Parse`); results and errors shown inline instead of as toasts only.

**Verification:** manual on a dev chat; if a service is added, a service test for the admin check.

- [x] Investigate and decide on the service boundary: `CopyChatToPlaceUI` only wraps `Chat_CopyChat` / `Chat_PublishCopiedChat` with modals and localized toasts, so no `IAdminChats` service is needed; the page runs the same commands through `ICommander`
- [x] Page (confirmation shows the real chat and place titles; failures show the correlation ID)

### Task 8: Digest page

**Files:** `src/dotnet/Mui/Features/Digest/DigestPage.razor`

Ports `DigestTestPage` on `IEmails.GetDigestPreview`: mode selector (the viewer's unread chats, or selected chats with an "as of" time), chat ids as a comma-separated list, "Generate preview", the unsubscribe-link note, and two `MudTabs`: the rendered email in a sandboxed `iframe srcdoc`, and per-chat diagnostics (chat link, unread count, bullet points, "+N other unread chats"). The old "Pick chats" button opens the app's `ForwardMessageModal`, which is app UI and is not reused; the id list field replaces it.

**Verification:** manual against a dev account; unit test for chat-id list parsing.

- [x] Page and both modes (the "as of" time is entered and sent as UTC)

### Task 9: Remove migrated pages, document

**Files:**
- Delete: `FlowsTestPage.razor`, `MeshTestPage.razor`, `SystemTestPage.razor`, `AdminCopyChatToPlacePage.razor`, `DigestTestPage.razor`, and any links/menu entries to them; delete `CopyChatToPlaceUI` pieces no longer used
- Create: `docs/ui/mui.md` (purpose, hosting decision, how to add a section, theme, the English-only exception, the reference rule); add to `docs/ui/index.md`
- Modify: `docs/architecture/project-structure.md` (new project row), `docs/plans/index.md` (this plan under Active)
- Regenerate: `docs/api-index*.md` with the repo's generator if `Mui` types should be listed

**Verification:** `dotnet build ActualChat.CI.slnf`; grep shows no references to the removed pages; the app's own test pages index no longer lists them.

- [x] Remove old pages after the Mui versions are verified in a running server (all five checked in Chrome; the nav entries are gone too)
- [x] Docs (`docs/ui/mui.md`, UI index, sidebar, project structure)

## Resolved questions

1. English-only Management UI: confirmed, recorded as an exception to the localization rule.
2. `/m/` prefix, not `m.voxt.ai`: confirmed. A subdomain would need a task for cookie domain, ingress, and CORS first.
3. `DigestTestPage` moves too (task 8).

## Risks

- **Two Razor roots in one host** do not work (found by running the server); the single-root design replaced it.
- **CSP**: `ContentSecurityPolicy.cs` may block MudBlazor's inline styles or fonts; handled in task 1.
- **Server-side state in `Diagnostics`/`FlowBackend` paths** is unchanged, so behavior risk is low; the main risk is porting UI behavior faithfully (flow stale-response guard, expanded-row live state).
- **Not testable in `/server-loop` by me yet**; you will hand it over after the plan is approved and tasks land. Until then verification is build-only plus unit tests for view-model logic.

## Changes after review

- **Name:** the project, folder and namespace are `Mui` / `ActualChat.Mui` (Management UI); three-letter abbreviations are PascalCase in .NET, like `Sql`. The UI calls itself "Voxt Management UI". Types use the `Mui` prefix (`MuiApp`, `MuiLayout`, `MuiAccess`, `MuiGate`, `MuiActions`, …); `MuiAccess` is `SignedOut` / `Denied` / `Granted`. `Mui` is a word in the solution dictionary, and `Directory.Build.props` classifies the project as a Blazor (UI) project.
- **Look:** the app bar uses the theme's primary color; no logo in the app bar; light theme only, no toggle (nothing stored the choice); 16px corner radius so buttons are rounder; menu is Overview first, then alphabetical; the drawer ends with a GitHub link and a gray Voxt logo that leads to the main site.
- **Hosting:** one Razor root, see task 1.
- **Menu:** Dashboard (front page, key metrics later), Reports (the main section: users, chats, messages, ...; a common report structure comes with the first reports) and System (one page per subsystem or tool: Digest, Flows, Host, Mesh, Tools; each shows reports and offers actions). The old "System" page is "Host" and every System page is under `/m/system/`.
- **Server-side only:** Mui may call backend services and run backend commands directly, so it is its own authorization layer and WASM is no longer a design goal; see [the Mui document](../ui/mui.md#server-side-only).
