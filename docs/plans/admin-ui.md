# Admin UI Implementation Plan

**Goal:** A separate, web-only admin front-end under `/m/` ("manage"), built on MudBlazor and Blazor Server, that takes over the admin pages now living in the app's `/test/*` pages: flow monitoring, system commands, mesh info, the chat-to-place copy tool, and the digest preview. It shares no UI code, JS, or CSS with the app UI.

**Architecture:** A new Razor class library `UI.Admin` is referenced by `App.Server`, which maps it at `/m/` with its own HTML shell and the interactive Server render mode only. Components inject contract interfaces (`IDiagnostics`, `ISystemProperties`, `IAccounts`, commands via `ICommander`) and never server-only types, so adding a WASM render mode later does not require rewriting them. Access is limited to admin accounts through the existing session cookie.

**Tech Stack:** C# 15, Blazor Server, MudBlazor, Fusion compute state, xUnit.

## Scope

**In (phase 1: move what exists):**

| Source page | Admin page | Notes |
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
3. **Hosted in the `App.Server` process**, not a new executable. The admin pages call `IDiagnostics` and the system commands, which are server-side services; in-process, they run without extra RPC hops, and the mesh fan-out in `Diagnostics.GetMeshDiagInfo` keeps working as is.
4. **Isolated shell and bundle.** The admin root page renders its own `<html>`, loads only MudBlazor's static assets plus one `admin.css` and `blazor.web.js`. It does not load the app bundle, Tailwind CSS, or app TypeScript.
5. **English-only UI.** `docs/CODING_STYLE.md` requires every user-visible string to come from the localization catalog. The admin UI is for staff, and the pages it replaces already hardcode English. This plan treats it as an explicit exception and records it in `docs/ui/admin.md`; confirmed.
6. **Feature folders registered through one nav registry.** Each feature (`Flows`, `System`, `Mesh`, `Tools`) is a folder with its page(s) and a single `AdminNavEntry` registration. A new section is a new folder plus one registration line.
7. **Migrated pages are removed from the app UI** once the admin versions are verified, so there is one place for each tool.

## Reuse

### Existing abstractions to reuse

- `IDiagnostics` / `Diagnostics` (`Api.Contracts/Chat/IDiagnostics.cs`, `Chat.Service/Diagnostics.cs`): mesh info, flow stats, flow list, flow details. Already admin-checked server-side through `RequireAdmin`; the UI check is only for navigation.
- `ISystemProperties` and the `SystemProperties_InvalidateEverything` / `SystemProperties_PruneComputedGraph` commands (`Api.Contracts/Users/ISystemProperties.cs`).
- `IAccounts.GetOwn(Session)` and `AccountFull.MustBeAdmin` for the access gate, the same check `RequireAccount MustBeAdmin` makes.
- `ComputedStateComponent<T>` from `ActualLab.Fusion.Blazor` for live state (`GetFlowDetails`, `GetMeshDiagInfo` are compute methods), instead of `AppUIHub`-based components.
- `ISessionResolver`, `AuthHelper.UpdateAuthState` and the session cookie handling from `RootServerPage.razor` (`Initialize()`): the admin root reuses the same session establishment.
- `HostId`, `HostInfo`, `ComputedRegistry`, `ReloadUI`-free parts of `SystemTestPage`.
- `CopyChatToPlaceUI.CopyChat` / `PublishCopiedChat` and `ChatsBackend.CopyChat`: logic is reused; the UI-hub-bound part is replaced (task 7).
- `docs/ui/components.md` conventions for file layout and CSS class naming (`c-` prefix), applied inside `UI.Admin`.

Searched `docs/api-index.md` and `api-index-full.md` for admin, diagnostics, dashboard, table and grid helpers; nothing reusable beyond the list above. `UI.Blazor` components (`Button`, `Form`, `MainHeader`, `CopyTrigger`, `RequireAccount`) are deliberately not reused: they depend on the app's styling, `AppUIHub`, and bundle, which this project isolates.

### Reusability of new components

| New component | Placement | Why |
|---|---|---|
| `UI.Admin` (pages, layout, theme, nav registry) | New project `UI.Admin` | Admin-only; keeping it out of `UI.Blazor*` is the point of the work |
| `AdminComponentBase`, `AdminComputedComponent<T>` (session, services, error reporting) | `UI.Admin` | Depend on MudBlazor; useful only to admin pages |
| Flow-status/diagnostic view models (formatting, status → severity) | `UI.Admin`, plain classes with no MudBlazor types | Unit-testable now; movable to `ActualChat.Core` if another host needs them |
| Admin-only data the pages need that doesn't exist yet (see task 7) | `Api.Contracts` + service project (`IAdmin…` frontend service) | Contracts are what keep the pages WASM-ready |

No TypeScript is added.

## Global constraints

- `UI.Admin` references contract and API projects only, never `*.Service` projects, `App.Server`, or `UI.Blazor*`. This is what keeps a later WASM render mode possible, and a build-time reference check enforces it (task 1).
- Admin access is enforced by the services (`RequireAdmin`), not only by the UI gate. New admin endpoints/services must call it.
- `test-…@actual.chat` accounts never become admin (existing rule); the plan does not touch how admin status is granted.
- Follow `docs/CODING_STYLE.md` (no `Async` suffix, no XML docs, line length); read `docs/ui/components.md` before the first component.

## Tasks

Each task is one commit. Tasks 1–2 are a gate: if the hosting spike (task 1) fails, stop and revisit decision 3 before building pages.

### Task 1: `UI.Admin` project and hosting spike

**Files:**
- Create: `src/dotnet/UI.Admin/UI.Admin.csproj` (Razor SDK, `MudBlazor` package, project references to `Api`, `Api.Contracts` only)
- Create: `src/dotnet/UI.Admin/AdminRoot.razor` (HTML shell), `Routes.razor`, `_Imports.razor`
- Modify: `Directory.Packages.props` (add `MudBlazor`), `src/dotnet/App.Server/App.Server.csproj`, `src/dotnet/App.Server/Module/AppServerModule.cs`, `ActualChat.sln`, `ActualChat.CI.slnf`
- Modify: `src/dotnet/App.Server/Module/ContentSecurityPolicy.cs` if MudBlazor's assets or inline styles are blocked

**What to prove:** `GET /m/` returns the admin shell, interactive Server circuit connects, MudBlazor renders, and the app root at `/` still works unchanged.

**Hosting options, in the order to try:**

1. A second `app.MapRazorComponents<AdminRoot>().AddInteractiveServerRenderMode()` with `AddAdditionalAssemblies(typeof(AdminRoot).Assembly)`, branched on `/m`. Cleanest if two roots can share `blazor.web.js` and the `/_blazor` hub.
2. If two roots conflict: make `RootServerPage` render `AdminRoot` when the path starts with `/m/`, and exclude the admin assembly from the app router. Same behavior, one root.

Record which option works in `docs/ui/admin.md`.

**Verification:** `dotnet build ActualChat.CI.slnf`; a script (or MSBuild target) that fails if `UI.Admin` references a `*.Service`, `App.*`, or `UI.Blazor*` project; manual: `/m/` loads, `/` loads, hot reload unaffected. (The `HostInfo.IsTested` guard skips Razor mapping in tests, so there is no integration test for this; the manual check is the gate.)

- [ ] Create project, add shell and a placeholder page
- [ ] Wire hosting (option 1, fall back to option 2)
- [ ] Add the project-reference guard
- [ ] Confirm `/` and `/m/` both serve

### Task 2: Session, admin gate, and base components

**Files:**
- Create: `src/dotnet/UI.Admin/Services/AdminSessionInitializer.cs` (or inside `AdminRoot`): establishes the session the way `RootServerPage.Initialize()` does
- Create: `src/dotnet/UI.Admin/Components/AdminComponentBase.cs`, `AdminComputedComponent.cs`, `RequireAdmin.razor`
- Create: `src/dotnet/UI.Admin/Pages/NotAdmin.razor`

**Behavior:**
- Signed-out visitor → link to the main site's sign-in, returning to `/m/`.
- Signed-in non-admin → a 403-style page that shows nothing about the admin features.
- Admin → layout renders.

`AdminComputedComponent<T>` wraps `ComputedStateComponent<T>` with `Session`, error display through `ISnackbar`, and a `Refresh` helper, so pages like Flows don't repeat that.

**Verification:** unit tests for the gate's decision logic (account null / not admin / admin), kept as a plain class; manual check with an admin and a non-admin account.

- [ ] Session establishment shared with the app's approach
- [ ] `RequireAdmin` gate and `NotAdmin` page
- [ ] Base components

### Task 3: Layout, theme, navigation registry

**Files:**
- Create: `src/dotnet/UI.Admin/Layout/AdminLayout.razor`, `NavMenu.razor`
- Create: `src/dotnet/UI.Admin/Theme/AdminTheme.cs` (the `MudTheme`), `src/dotnet/UI.Admin/wwwroot/admin.css`
- Create: `src/dotnet/UI.Admin/Navigation/AdminNavEntry.cs`, registration extension

**Details:**
- `MudTheme` palette uses Voxt's primary/accent colors (take the values from the app's design tokens, `tailwind.config.js` / `src/dotnet/UI.Blazor.App/styles.css`; copy the values, don't import the files). Light and dark palettes; logo in the app bar. Beyond that, MudBlazor defaults.
- `AdminNavEntry(Title, Href, Icon, Order)` registered in DI; `NavMenu` renders the sorted list. Features register themselves in their own `AddXxx` extension.

**Verification:** build; visual check of the layout in light and dark.

- [ ] Layout with app bar, drawer, logo
- [ ] Theme
- [ ] Nav registry; placeholder entries for the four features

### Task 4: System page

**Files:** `src/dotnet/UI.Admin/Features/System/SystemPage.razor` (+ `.cs`)

Ports `SystemTestPage`: host id, kind, roles; invalidate everything (locally / on this front-end / everywhere); prune computed graph (same three); GC ×3. Destructive actions ask for confirmation through `IDialogService` (the old page had none). Drops "Reload UI" and "Throw Exception".

**Verification:** manual: each action runs and reports success or failure in a snackbar; "locally" is blocked on a server host with the same message as today.

- [ ] Page and actions
- [ ] Confirmation dialogs

### Task 5: Mesh page

**Files:** `src/dotnet/UI.Admin/Features/Mesh/MeshPage.razor`, `MeshNodeView.razor`

Ports `MeshTestPage` using `GetMeshDiagInfo` through `AdminComputedComponent<MeshDiagInfo?>`: a card per node (this node, then `Others`) with nodes table (state, roles, endpoint), mesh RPC refs table, RPC peers table with collapsible connection state (opened when not Connected). Refresh generates a new tag as today. "Copy report" produces the same text report as the old page, generated from the model rather than scraped from `innerText`.

**Verification:** manual against a multi-node setup if available, otherwise single node; unit test for report text generation.

- [ ] Tables
- [ ] Refresh and report copy

### Task 6: Flows page

**Files:** `src/dotnet/UI.Admin/Features/Flows/FlowsPage.razor`, `FlowDetailsPanel.razor`, `FlowStatusChip.razor`

Ports `FlowsTestPage`:
- `MudDataGrid` for per-type stats (Total / Completed / Scheduled / Idle+Stuck / Failed); row click filters instances; failed and stuck counts highlighted.
- Instances grid with "Problematic only" and "Hide completed"; 200-row limit as today, shown in the footer; stale-response guard (generation counter) kept.
- Row expansion shows the console log and error through `GetFlowDetails` as live state for expanded rows only (as the page does now).
- Deep-linkable state: selected type and filters in the query string, so a link to a problem is shareable.

**Verification:** manual against dev data; unit test for the status-to-severity mapping and filter-to-query mapping.

- [ ] Stats grid
- [ ] Instances grid, filters, expansion
- [ ] Query-string state

### Task 7: Tools page (copy chat to place)

**Investigation first:** `CopyChatToPlaceUI.CopyChat` / `PublishCopiedChat` are in the app UI project and take `AppUIHub`. Check what they do (command calls, confirmation, progress). If they only run commands, call the same commands from the admin page through `ICommander`. If they depend on app-UI services, add an admin-only frontend service (`IAdminChats` in `Api.Contracts`, implementation in `Chat.Service`, `RequireAdmin` inside) and make both callers use it.

**Files:** `src/dotnet/UI.Admin/Features/Tools/CopyChatToPlacePage.razor`; possibly `Api.Contracts/Chat/IAdminChats.cs` and `Chat.Service/AdminChats.cs`

Form with chat id and place id, validated through `MudForm` and the id parsers (`ChatId.Parse`, `PlaceId.Parse`); results and errors shown inline instead of as toasts only.

**Verification:** manual on a dev chat; if a service is added, a service test for the admin check.

- [ ] Investigate and decide on the service boundary
- [ ] Page

### Task 8: Digest page

**Files:** `src/dotnet/UI.Admin/Features/Digest/DigestPage.razor`

Ports `DigestTestPage` on `IEmails.GetDigestPreview`: mode selector (the viewer's unread chats, or selected chats with an "as of" time), chat ids as a comma-separated list, "Generate preview", the unsubscribe-link note, and two `MudTabs`: the rendered email in a sandboxed `iframe srcdoc`, and per-chat diagnostics (chat link, unread count, bullet points, "+N other unread chats"). The old "Pick chats" button opens the app's `ForwardMessageModal`, which is app UI and is not reused; the id list field replaces it.

**Verification:** manual against a dev account; unit test for chat-id list parsing.

- [ ] Page and both modes

### Task 9: Remove migrated pages, document

**Files:**
- Delete: `FlowsTestPage.razor`, `MeshTestPage.razor`, `SystemTestPage.razor`, `AdminCopyChatToPlacePage.razor`, `DigestTestPage.razor`, and any links/menu entries to them; delete `CopyChatToPlaceUI` pieces no longer used
- Create: `docs/ui/admin.md` (purpose, hosting decision, how to add a section, theme, the English-only exception, the reference rule); add to `docs/ui/index.md`
- Modify: `docs/architecture/project-structure.md` (new project row), `docs/plans/index.md` (this plan under Active), `docs/CODING_STYLE.md` (localization exception for `UI.Admin`)
- Regenerate: `docs/api-index*.md` with the repo's generator if `UI.Admin` types should be listed

**Verification:** `dotnet build ActualChat.CI.slnf`; grep shows no references to the removed pages; the app's own test pages index no longer lists them.

- [ ] Remove old pages after the admin versions are verified
- [ ] Docs

## Resolved questions

1. English-only admin UI: confirmed, recorded as an exception to the localization rule.
2. `/m/` prefix, not `m.voxt.ai`: confirmed. A subdomain would need a task for cookie domain, ingress, and CORS first.
3. `DigestTestPage` moves too (task 8).

## Risks

- **Two Razor roots in one host** may not work (task 1 spike); fallback is described and costs little.
- **CSP**: `ContentSecurityPolicy.cs` may block MudBlazor's inline styles or fonts; handled in task 1.
- **Server-side state in `Diagnostics`/`FlowBackend` paths** is unchanged, so behavior risk is low; the main risk is porting UI behavior faithfully (flow stale-response guard, expanded-row live state).
- **Not testable in `/server-loop` by me yet**; you will hand it over after the plan is approved and tasks land. Until then verification is build-only plus unit tests for view-model logic.
