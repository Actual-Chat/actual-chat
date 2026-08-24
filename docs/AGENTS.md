# AI Agent Instructions for Voxt (formerly Actual Chat) repository

## Scope

This file applies to the entire repository: all code, documentation, 
and scripts under this directory tree.

IMPORTANT: See `README.md` in the root folder to learn what Voxt (formerly Actual Chat) is.

## Technology Stack

- **Language and Platform**: the project is compiled with .NET 11 (and C# 15)
- **Databases**: PostgreSQL, Redis. See `docker-compose.yml.
- **UI**: mostly Blazor, with a decent amount of TypeScript.
- **Testing**: all tests are based on xUnit.

## Project Structure

- Main solution: `ActualChat.sln`
- Files are organized as:
  - `src/dotnet/`: .NET source code; `*.ts` files there are TypeScript counterparts for Blazor components
  - `src/nodejs/`: shared TypeScript code.
  - `tests/`: test projects.
  - `build/`: Bullseye-based `Build.csproj` - a project responsible for a set of advanced build tasks (e.g., building NuGet packages).
  - `artifacts/`: various build outputs / artifacts.

For detailed project organization, see [`docs/architecture/project-structure.md`](./architecture/project-structure.md).

When implementing new features, see [`docs/development/implementing-features.md`](./development/implementing-features.md) for a comprehensive guide covering all layers from domain models to UI components.

When creating UI components, see [`docs/development/ui-components.md`](./development/ui-components.md) for file structure, CSS naming, and styling conventions.

When adding or changing any user-visible text, see [`docs/i18n.md`](./i18n.md) — strings come from the localized catalog, and the exceptions that stay English are enumerated there.

## Build Prerequisites

- Install .NET 11 (preview)
- Run:
  ```powershell
  dotnet restore
  dotnet tool restore
  npm-install.cmd
  ```

## Building

The most important files related to build process are:
- `*.sln` and `*.csproj` files
- `Directory.Build.props` (also located in some of sub-folders) and `Directory.Build.targets` files
- `Directory.Packages.props` file listing versions of C# project dependencies.
- You can also look at `.github/workflows/build-test-deploy-dev.yml` and `.config/dotnet-tools.json`.

### C# Build

- To build the main solution, use:
  ```powershell
  dotnet build
  ```

- You can build individual projects by specifying their `.csproj` file:
  ```powershell
  dotnet build path/to/project.csproj
  ```

### TypeScript Build

**Important:** `dotnet build` does NOT build TypeScript files. TypeScript has a separate build process.

- To build TypeScript (debug mode):
  ```powershell
  ./npm-build.cmd
  # Or manually:
  npm ci        # Install dependencies (run once, or after package.json changes)
  npm run build:Debug
  ```

- To build TypeScript (release mode):
  ```powershell
  ./npm-build-release.cmd
  # Or manually:
  npm run build:Release
  ```

- TypeScript source files are in:
  - `src/nodejs/` - shared TypeScript code
  - `src/dotnet/**/*.ts` - TypeScript counterparts for Blazor components

## Testing

Tests are located under `tests/`.

- To run all tests:
  ```powershell
  docker compose up -d --build --wait
  dotnet test ActualLab.Fusion.sln
  ```
- To run a specific .csproj with xUnit tests:
  ```powershell
  dotnet test src/tests/<TestProjectName>/<TestProjectName>.csproj
  ```

## Coding Conventions

See [`CODING_STYLE.md`](./CODING_STYLE.md) for detailed coding style guidelines.

## Pull Request Messages

- When creating a PR, include a brief summary of changes with a standard "feat:", "fix:", "refactor:", "chore:", or "docs:" prefix.
- Reference related issues or discussions if applicable.

## Development Loop in Docker

The host runs `./run-watch.cmd` — it auto-rebuilds and restarts the server when you change files.

After editing code, poll `tmp/watch-dotnet.log` until you see `Now listening on:` (ready) or `error` (fix and wait again). Do not use `/server-start` or `/server-restart` — the watch process owns the server.

Frontend build output: `tmp/watch-web.log`.

## Programmatic Checks

- After making C# changes, run `dotnet build ActualChat.sln` to verify they don't break the build.
- After making TypeScript changes, run `npm run build:Debug` (from project root) to verify they compile.
- Ensure all builds pass before submitting changes.

## Type Catalog

Use `docs/api-index.md` first to quickly check whether an existing abstraction covers your need — it's a condensed reference (~170 types) of the most important types, structured by project. For the complete list of all public types (~770 types), see `docs/api-index-full.md`.

## Additional Notes

`AGENTS.md` or `/agents/*.md` in other folders may extend and override instructions provided here.
