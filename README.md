# Actual Chat

![unit tests](https://github.com/Actual-Chat/actual-chat/actions/workflows/unit-tests.yml/badge.svg)




## Prerequisites




Install:
- [Git](https://git-scm.com/downloads)
- [.NET 6](https://dotnet.microsoft.com/download/dotnet/6.0)
- `dotnet workload install wasm-tools`  
- [Docker](https://www.docker.com/get-started)
- [NodeJS 16+](https://nodejs.org/en/) and the latest `npm`;
  [npm-windows-update](https://www.npmjs.com/package/npm-windows-upgrade)
  is the simplest way to update `npm` on Windows
- [Edge](https://www.microsoft.com/en-us/edge#platform)
  or [Chrome](https://chromeenterprise.google/browser/download/)
- [Playwright](https://playwright.dev/docs/intro/#installation)
- [Configure GCP service key](https://www.notion.so/actual-chat/GCP-service-keys-d4cbb93a014644fba636e35aad45f94d)
  ensure time and timezone are configured correctly on a machine

Recommended IDEs:
- [Rider](https://www.jetbrains.com/rider/)
- [Visual Studio Code](https://code.visualstudio.com/)

## Build

First, get Google Cloud credentials (`key.json` file) to make sure you can use Google Transcribe APIs. Copy the provided file to `~/.gcp/key.json` and ensure `GOOGLE_APPLICATION_CREDENTIALS` env. variable stores its path.

To build & run the project:

```bash
# Start Docker containers for PostgreSQL, Redis etc.
docker-compose up

# Install dependencies and run watch (dotnet watch + webpack watch)
dotnet run --project build -- restore-tools npm-install watch
```

If you're getting `RpcException` with 
`"Request had invalid authentication credentials."` message,
make sure your time & time zone settings are correct.

Other useful commands:

```powershell
# What else build project can do?
dotnet run --project build -- --help
 
# List all available targets (you can combine them)
dotnet run --project build -- --list-targets

# Run with observability services (opentelemetry collector + jaeger) locally:
docker-compose -f docker-compose.observability.yml -f docker-compose.yml up

# Use either env. var or the matching option in your appsettings.local.json
$env:HostSettings__OpenTelemetryEndpoint="localhost"
docker run --project build -- watch
```

You can add your own targets (as C# code) to `./build/Program.cs`, which is actually a [Bullseye](https://github.com/adamralph/bullseye) build project written in C#.

It's also useful to have an [alias](https://github.com/vchirikov/dotfiles/blob/7f280e9287ceba6fd508577fb0665fc19e4d9b29/Microsoft.PowerShell_profile.ps1#L231-L249) to run build system (to run commands like `bs watch`).

There are some shortcuts in `*.cmd` files, you can use them too.  

## Conventions

We use:
- [Conventional commits](https://www.conventionalcommits.org/en/v1.0.0/)
- Standard .NET naming & style guidelines. Exceptions:
    - Place `{` on the same line for `if`, `for`, and
      any other code inside method body
    - If you have to wrap a method body expression,
      wrap it before `=>` rather than after 

## Release branches

Read [Nerdbank.GitVersioning docs](https://github.com/dotnet/Nerdbank.GitVersioning/blob/master/doc/nbgv-cli.md)  

```bash
# might not work, see https://github.com/dotnet/Nerdbank.GitVersioning/issues/685
nbgv prepare-release beta
```

We use the `alpha` suffix in the `master` branch, `beta`,`rc-*` in release branches. When a release branch drops the version suffix it becomes a production release.
