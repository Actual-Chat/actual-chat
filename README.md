# Actual Chat

![unit tests](https://github.com/Actual-Chat/actual-chat/actions/workflows/unit-tests.yml/badge.svg)

- Discord: https://discord.gg/W7JP3Gve
- Telegram: https://t.me/joinchat/ndwLjwvDnj5iY2Y7

## Prerequisites

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

Recommended IDEs:
- [Rider](https://www.jetbrains.com/rider/)
- [Visual Studio Code](https://code.visualstudio.com/)

## Build

First of all you should get google cloud credentials to transcribe audio.  
You should place `key.json` at `~/.gcp/key.json` (where `~` is a home for the user) and specify
`GOOGLE_APPLICATION_CREDENTIALS` env variable to that file.

To build & run the project:

```bash
# run necessary Docker containers with db, redis etc
docker-compose up
# install dependencies and run watch (dotnet watch + webpack watch)
dotnet run --project build -- restore-tools npm-install watch
```

If you got something like:

```
Grpc.Core.RpcException
  HResult=0x80131500
  Message=Status(StatusCode="Unauthenticated", Detail="Request had invalid authentication credentials. Expected OAuth 2 access token, login cookie or other valid authentication credential
  ...
```
Check your time & timezone settings.  


Other useful commands:

```powershell
# help of the build project
dotnet run --project build -- --help 
# list all available targets (you can combine them)
dotnet run --project build -- --list-targets
# run with observability services (opentelemetry collector + jaeger) locally:
docker-compose -f docker-compose.observability.yml -f docker-compose.yml up
# this for pwsh, specify env variable where you want or specify this setting inside your appsettings.local.json
$env:HostSettings__OpenTelemetryEndpoint="localhost"
docker run --project build -- watch
```

It's also useful to have [alias](https://github.com/vchirikov/dotfiles/blob/7f280e9287ceba6fd508577fb0665fc19e4d9b29/Microsoft.PowerShell_profile.ps1#L231-L249) to run build system (to run commands like `bs watch`).


p.s. we also have some shortcuts in `*.cmd` for now, you can use them if you want.  

p.p.s. we use [Bullseye](https://github.com/adamralph/bullseye) so you can add your targets (as C# code), check the code in `./build/Program.cs`  

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

We use the `alpha` suffix in the `master` branch, `beta`,`rc-*` in release branches.  
When a release branch drops the version suffix it becomes a production release.
