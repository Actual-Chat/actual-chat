# Voxt Management UI (Mui)

The Voxt Management UI is the staff console of Voxt: one web place to see what the service is doing,
what is going wrong, and to act on it. It lives at `/m/` ("manage"), is built with
[MudBlazor](https://mudblazor.com) on Blazor Server, and is shown to the user as "Voxt Management UI".
In code it is `Mui` (three-letter abbreviations are PascalCase in .NET, like `Sql`): the project
`src/dotnet/Mui`, the namespace `ActualChat.Mui`, the `Mui` prefix on types (`MuiApp`, `MuiLayout`,
`MuiGate`, ...).

Implementation plan and history: [Management UI plan](../plans/mui.md).

## What it is for

- **Seeing the state of the service.** Key metrics on the dashboard; reports on users, chats, messages
  and every other subsystem; enough to follow what happens in real time and to find what is going wrong
  before users report it. The raw metrics also go to Google Cloud, but they are hard to navigate there
  when nobody knows which of them matter; Mui is the curated view.
- **Acting on it.** Invalidate, prune, resume, copy a chat to a place, preview a digest: the operations
  that used to live in the app's `/test/*` pages.
- **Keeping both out of the app UI.** Staff tools in the app's pages ship in the WASM and mobile bundles
  and grow there. Mui has its own shell and no shared UI code, JS or CSS.

It is **not** part of the Voxt apps: web only, no embedding into the apps, no MAUI, no WASM render mode
(see "Server-side only" below). It is used by Voxt staff only, so it needs no localization, offline
mode, or mobile layout.

## Menu structure

| Section | What it holds |
|---------|---------------|
| **Dashboards** (`/m`, `/m/dashboards/...`) | The main section; its first page, Overview (`/m`), will show key metrics. One dashboard per area: Chats, Messages, Notifications, Sessions, Uploads, Users (user activity first: texts, voice, uploads, new chats and places). Places and other areas can follow. A dashboard runs SQL (sometimes with post-processing in code) and shows how a few metrics trend, so it is possible to follow what is happening. See "Dashboards" below. |
| **System** (`/m/system/...`) | One page per subsystem or tool (Digest, Events, Flows, Host, Mesh, Operations, SQL, Tools). A page shows the subsystem's reports **and** offers its actions next to them, so one page answers "what is going on" and "let me fix it". |

Within a group the pages are alphabetical; Overview is first. A section registers itself (see
"Adding a section").

## Key design decisions

| Decision | Why | Rejected |
|----------|-----|----------|
| **Blazor Server + MudBlazor** | The pages were already Razor and call C# Fusion services and commands, so porting is mostly markup; MudBlazor has the data grid, dialogs, snackbar and theme needed for staff tools, and Claude generates it reliably. | A TypeScript SPA (a full rewrite of every page); Radzen (wider set but plainer and harder to theme), Fluent UI Blazor (no charts). |
| **`/m/` prefix, not `m.voxt.ai`** | Same origin: the session cookie, `/signIn`, the `/_blazor` circuit, ingress and OAuth redirects all work unchanged. A subdomain needs a cookie domain (the session cookie is host-only), OAuth redirect URIs, DNS, a certificate and ingress. | `m.` subdomain. Moving later is a configuration change plus that work. |
| **Hosted inside the `App.Server` process** | Pages call services and run commands in-process, with no extra hops; the mesh fan-out of `Diagnostics` keeps working. | A separate executable. |
| **One Razor root** | A second `MapRazorComponents<T>()` makes `/_blazor/initializers` and the circuit endpoints match twice (`AmbiguousMatchException`), which breaks the main app. `Mui` is an additional assembly of the existing mapping and `RootServerPage` renders `MuiRootPage` for `/m` paths. | Two roots (tried; fails at runtime). |
| **Isolated shell and bundle** | `MuiRootPage` renders its own `<html>` and loads only MudBlazor's assets and `blazor.web.js`: no app bundle, Tailwind or TypeScript. Mixing them would couple the staff tool to app styling and make it harder for tools like Claude to work on either. | Reusing `UI.Blazor` components (`Button`, `Form`, `MainHeader`, ...): they depend on the app's styling and `AppUIHub`. |
| **English only** | A staff tool; the pages it replaced were already English. It is an explicit exception to the localization rule in [CODING_STYLE.md](../CODING_STYLE.md#localization-ui-strings) and [i18n.md](../i18n.md), and in `.claude/style-bypasses.md`. | Localizing it. |
| **Light theme only, no toggle** | One theme is enough, and nothing stored the choice anyway. The app bar uses the theme's primary color; buttons and cards have a 16px corner radius. MudBlazor has no named theme presets (one Material theme configured through `MuiTheme`: palette, typography, layout properties). Colors are copied from `src/nodejs/styles/colors.css`; the font is TT Commons Pro (the app's, served from `/dist/assets/woff2`, declared in `mui.css`) with the system stack as a fallback; Google Fonts are not allowed by the content security policy. | Dark mode and a toggle. |
| **Sign in on the main site** | The gate shows a hint and a link to Voxt; the same cookie then works on `/m/`. | A separate sign-in flow. |

## Server-side only

Mui runs only as Blazor Server, in the `App.Server` process, so its components can use everything the
server can: **they may call backend services and run backend commands directly** (the `*Backend`
interfaces from the `*.Contracts` projects, `ICommander` commands), not only the front-end services a
client can reach. Reports and tools use this to read and fix what no client API exposes.

That has consequences:

- **Mui is its own authorization layer.** Backend services trust their caller: they do not check a
  session or an admin flag, because only trusted code reaches them. A front-end service such as
  `Diagnostics` checks `RequireAdmin` itself; a backend call made from a Mui page checks nothing. So
  every Mui page must run behind `MuiGate` (`MuiLayout` wraps every page in it), and Mui must never
  expose a backend call through anything reachable without the gate: no HTTP endpoints, no static
  state. A new service written for Mui checks admin access itself, as front-end services do.
- **WASM is no longer a design goal.** Pages that use backends cannot run in the browser. Keep
  components that do not need a backend free of server-only types where it costs nothing, but do not
  design around a WASM mode.
- **The reference rule.** `Mui` references contract projects (`Api.Contracts`, `Chat.Contracts`, ...), the
  server infrastructure (`Core.Server`, `Db`) and public packages, but never a `*.Service`,
  `*.Migration`, `App.*` or `UI.Blazor*` project: implementations of other services are resolved
  through dependency injection. An MSBuild target in `Mui.csproj` fails the build otherwise.

## Hosting its own services

Mui may host services that exist for it (today: `IMuiDbBackend` and `IMuiDb`). `MuiModule` is a regular
module, and it registers by host role:

- **Backend part, on every host.** `IMuiDbBackend` is a distributed backend service (hosted by the
  `DiagnosticsBackend` role, which every backend host has, so no new role or shard scheme). Backend hosts
  run the implementation; API hosts get a routing client. The backend runs on backend hosts and **has
  access to the databases**.
- **Everything else, on API hosts only:** the UI, `IMuiDb` (access check and caching) and the
  per-circuit services.

Constraint: Mui services are deployed to API hosts and **cannot use a `DbContext` or a database
connection there**. Anything that needs a database goes through a backend service routed to a backend
host, as `IMuiDbBackend.RunQuery` does. The first parameter of a backend method says where it runs
(`GetDatabases` goes to a random backend, `RunQuery` to the one its SQL text hashes to:
`MuiSqlQuery` is `IHasShardKey`). Backend methods are plain RPC methods, not compute methods; caching is done in front of them.
Later the backend may split into parts (for example a SQL backend and a DB backend).

## Data: existing only

Mui reads what the databases already hold. **It does not extend the schema or add events to any
database, and it does not change how other services write data**, unless that is explicitly approved:
if a question can't be answered from existing data, the answer is "not available", with the closest
approximation stated on the page. (Example: logins and sign-outs are not logged, so the Sessions
dashboard counts new sessions as logins, and sign-outs as sessions that were deactivated.)

## Database access

`IMuiDbBackend` (backend, routed by its first parameter, `GetDatabases` takes a
`RandomShardRef`, `RunQuery` takes a `MuiSqlQuery` whose `ShardKey` is the hash of its SQL) and `IMuiDb` (admin-only front on API hosts, adds caching) give two things:

- **Schema.** Every database a host has (`chat`, `users`, `contacts`, ...): tables, columns, types and
  the EF-generated CREATE script, written so that a person or an LLM can build queries from it.
  Databases are named by their context; with multi-tenancy a database would be `users-0123` and the
  schema would list its tenants.
- **Queries.** Plain SQL, single statement, read-only, at most 500 rows (the backend never returns more and
  flags the result as truncated when a 501st row exists; `MuiSqlQuery.MaxRowsLimit`). `IMuiDb.Query` takes how long to cache the result
  (zero = no caching); the cache key is the query, or an explicit `CacheKey` (dashboards use one that
  doesn't depend on the current time). Dashboards cache for 10 minutes; "Reset cache" clears it.

The same two operations are exposed by the MCP tools `get_database_schema` and `run_sql_query` (admin
only, no caching), which lets an agent query production through the existing MCP server. The SQL page
under System is the same API with a UI; it accepts `?db=` and `?sql=`.

How read-only is enforced (every layer matters; none is relied on alone):

1. **One statement.** `MuiSqlGuard` scans the SQL (comments, quoted strings, `E'..'` escapes, quoted
   identifiers, dollar quotes) and rejects anything with a second statement or no statement.
2. **A connection of its own.** The query never touches a `DbContext` or its connection. Npgsql keeps one
   pool per connection string, and Mui's (application name `mui-readonly`, startup option
   `default_transaction_read_only=on`, at most 4 connections) is used by nothing else, so a Mui
   connection is never shared with the app. Npgsql resets the session before a connection goes back.
3. **Read-only from the first packet.** The startup option makes every transaction on these connections
   read-only; each query also runs in an explicit `SET TRANSACTION READ ONLY` transaction that is always
   rolled back, with `statement_timeout` set, and the code checks that both settings are really `on`.
4. **Only preparable statements.** The statement is sent through `PREPARE`, which accepts a single
   SELECT, INSERT, UPDATE, DELETE, MERGE or VALUES, so DDL, `SET`, `COPY`, `DO`, `CALL` and transaction
   control are rejected by the server; data-changing ones fail in the read-only transaction.

What is not covered: a read-only transaction doesn't stop a SELECT from calling a function with side
effects or reading server files if the database role can. The role Mui uses is the application's, so
treat this as "an admin can read everything the app can read". A verifier (an LLM that rejects queries or
masks sensitive result columns) is expected to sit in front of the MCP and custom queries later.

## Dashboards

A dashboard is a set of metrics; each metric is a SQL scalar over a time window (`{from}`, `{to}`).
Every dashboard shares one **period control** (kept per user in the browser, shared by all dashboards):

- **Interval:** day or week. Windows are rolling and end now ("last 24 hours", "last 7 days").
- **Compare to:** the same window a week, a month or a quarter earlier, or the previous interval.
- **Periods back:** 1 to 12 earlier windows, so a trend of up to a year of months.

Each metric is one chart card (bars or lines, a toggle; one color, no legend), laid out in a grid of at most
three cards per row and at least one, the cards of a row sharing the whole width. Every point is labeled
with its period and value and, except the oldest one, its change against the next older point
(`-1mo: 20 (+15%)`, `now: 10 (-3%)`). The bars are one color; only the labels on top are green for an increase and red for a decrease. Dashboards don't update by themselves; there is a Refresh
button, "Reset cache" (drops the 10-minute cache) and an "Auto update" switch that reruns every 10 minutes
(when the cache expires). Times are shown in the browser's time zone ("Updated 18:20:30 (just now)", the
latest window's range), and "X ago" comes from `IFusionTime`. A metric may declare how far back its data
exists (for example sign-outs); older windows show "-".
Other intervals can be added to the same control later. System pages show the current state and have no
period control. Events and Operations are live: per database, sizes and rates (events and operations per
second over the last 60 s and 5 min), refreshed every 5 s with no cache; Events drills down to the events
of one type.

## Access

- `MuiRootPage` establishes the Fusion session the way the app does and hands the circuit a session
  token; `MuiApp` decrypts it.
- `MuiGate` maps the account to `MuiAccess`: `SignedOut` (hint with a link to Voxt), `Denied` (not an
  admin), `Granted` (an admin account). The menu is hidden unless access is granted.
- On a local development instance the `test-*@actual.chat` accounts are admins, so
  `debugUI.signIn('test-admin1@actual.chat')` is the way to get in while developing.

## Layout of the code

| Part | Where | Role |
|------|-------|------|
| `Mui` | `src/dotnet/Mui/` | Razor class library: `MuiApp`, layout, theme, navigation, pages |
| `MuiRootPage` | `src/dotnet/App.Server/Components/Pages/` | HTML shell and session setup; in `App.Server` because `AuthHelper` is in `Users.Service` |
| Hosting | `AppServerModule`, `RootServerPage` | `Mui` is an additional assembly of `MapRazorComponents<RootServerPage>()`; `MuiModule` registers services |
| `Mui.UnitTests` | `tests/Mui.UnitTests/` | Gate, filters, parsers, report formatting |

`<base href>` is `/`, as in the app shell: Blazor matches page templates against the path relative to
the base, so every Mui page declares an `@page "/m/..."` route. The service worker (scope `/`) has no
`fetch` handler, so it does not intercept `/m/` navigations. Tests skip Razor mapping
(`HostInfo.IsTested`), so hosting is verified by running the server.

`AppHost.ValidateContainerRegistrations` skips `MudBlazor` types: MudBlazor registers transient
disposables, which the host otherwise rejects; here they are scoped to the circuit.

## Adding a section

1. Create `Features/<Name>/` with the page (`@page "/m/<group>/<name>"`, inheriting `MuiComponent`, or
   `MuiComputedComponent<T>` for live Fusion state).
2. Add `<Name>Feature.cs` with an `AddMui<Name>()` extension that calls
   `services.AddMuiNavEntry(new("Title", "/m/<group>/<name>", Icons.Material.Filled.<Icon>,
   Group: MuiNavGroups.<Group>))`; a new group is registered with `AddMuiNavGroup`.
3. Call it from `MuiModule.InjectServices`.
4. Put anything that can be tested without MudBlazor (filters, parsers, report text) in a plain class
   and test it in `Mui.UnitTests`.

`MuiActions` runs a command (or a command with a result) and reports failures in a snackbar; use it for
actions instead of handling exceptions per page.

## Sections today

| Page | Route | What it does |
|------|-------|--------------|
| Overview | `/m` | Placeholder for key metrics |
| Chats | `/m/dashboards/chats` | New group chats, direct chats, threads, place chats, places, chat joins |
| Messages | `/m/dashboards/messages` | Text messages, voice messages, voice minutes, active authors |
| Notifications | `/m/dashboards/notifications` | Notifications sent, new devices, active devices |
| Sessions | `/m/dashboards/sessions` | Active sessions, active signed-in sessions, new sessions (logins), sign-outs, compared over time |
| Uploads | `/m/dashboards/uploads` | Pictures, videos, files and links added to chats |
| Users | `/m/dashboards/users` | Active users, new accounts (sign-ups), users who sent messages, users who spoke |
| Digest | `/m/system/digest` | Digest email preview for the viewer's unread chats or selected chats |
| Events | `/m/system/events` | Per database: event table size, pending and delayed events, creation rate; drill-down by event type. Refreshes every 5 s, no cache |
| Flows | `/m/system/flows` | Stats per flow type, instances with filters kept in the query string, console and error per instance |
| Host | `/m/system/host` | Host info; invalidate everything, prune the computed graph, GC |
| Mesh | `/m/system/mesh` | Nodes, mesh RPC refs, RPC peers, copyable report |
| Operations | `/m/system/operations` | Per database: operation log size, operations per second, oldest and newest row. Refreshes every 5 s, no cache |
| SQL | `/m/system/sql` | Schema browser and a read-only SQL console |
| Tools | `/m/system/tools` | Copy a chat to a place, publish the copied chat |
