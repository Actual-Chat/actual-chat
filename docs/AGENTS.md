# AI Agent Instructions for Voxt (formerly Actual Chat) repository

## Scope

This file applies to the entire repository: all code, documentation, 
and scripts under this directory tree.

IMPORTANT: See `README.md` in the root folder to learn what Voxt (formerly Actual Chat) is.

## Technology Stack

- **Language and Platform**: the project is compiled with .NET 10 (and C# 13)
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

## Build Prerequisites

- Install .NET 10
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

- To build the main solution, use:
  ```powershell
  dotnet build
  ```

- You can build individual projects by specifying their `.csproj` file:
  ```powershell
  dotnet build path/to/project.csproj
  ```

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

## Programmatic Checks

- After making changes, run at least `dotnet build ActualChat.sln` to verify they at least don't break the build.
- Ensure all builds pass before submitting changes.

## Additional Notes

`AGENTS.md` or `/agents/*.md` in other folders may extend and override instructions provided here.
