# Actual Chat

![unit tests](https://github.com/Actual-Chat/actual-chat/actions/workflows/unit-tests.yml/badge.svg)

## Discord

- If you're a part of the official team, please contact @alexyakunin to get access.

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
./docker-start.cmd

# Install dependencies and run watch (dotnet watch + webpack watch)
./run-build.cmd restore-tools npm-install watch
```

If you're getting `RpcException` with 
`"Request had invalid authentication credentials."` message,
make sure your time & time zone settings are correct.

Other useful commands:

```powershell
# What else build project can do?
./run-build.cmd --help
 
# List all available targets (you can combine them)
./run-build.cmd -- --list-targets

# Run with observability services (opentelemetry collector + jaeger) locally:
docker-compose -f docker-compose.observability.yml -f docker-compose.yml up

# Use either env. var or the matching option in your appsettings.local.json
$env:HostSettings__OpenTelemetryEndpoint="localhost"
./run-build.cmd -- watch
```

You can add your own targets (as C# code) to `./build/Program.cs`, which is actually a [Bullseye](https://github.com/adamralph/bullseye) build project written in C#.

It's also useful to have an [alias](https://github.com/vchirikov/dotfiles/blob/7f280e9287ceba6fd508577fb0665fc19e4d9b29/Microsoft.PowerShell_profile.ps1#L231-L249) to run build system (to run commands like `bs watch`).

There are some shortcuts in `*.cmd` files, you can use them too.  

## Access on https://local.actual.chat
### Prerequisites (only once)
```bash
./add-hosts.cmd
```
<details>
    <summary>OR add hosts and trust certificate manually</summary>

- Add line with ```local.actual.host``` aliases to [Hosts file](https://www.howtogeek.com/howto/27350/beginner-geek-how-to-edit-your-hosts-file/).
 ```
 127.0.0.1  local.actual.chat media.local.actual.chat cdn.local.actual.chat
 ```
 - Import certificate [local.actual.chat.crt](./.config/local.actual.chat/ssl/local.actual.chat.crt) to "Trusted Root Certification Authorities". You can do it with [Microsoft Management Console](https://www.thesslstore.com/knowledgebase/ssl-install/how-to-import-intermediate-root-certificates-using-mmc/#import-root-certificate-using-mmc12/) or [Chrome](https://www.pico.net/kb/how-do-you-get-chrome-to-accept-a-self-signed-certificate/).

</details>

### Usage
 - Start Docker containers for reverse proxy and image proxy.
```
./docker-start.cmd
 ```
 - Run Actual-chat app.
 - Navigate with browser to https://local.actual.chat/

## Publish MAUI app
### Windows platform
- install certificate [sign_app.cer](./.config/maui/sign_app.cer) to "Trusted Root Certification Authorities" (required only once).
- build solution:
  - with IDE or,
  - run `msbuild` from repo root or,
  - run `dotnet build` from repo root;
- run publish command from repo root:<br>
  `dotnet publish src/dotnet/ClientApp/ClientApp.csproj -f net6.0-windows10.0.19041.0 -c Debug-Maui --no-restore -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true -p:PackageCertificateThumbprint=0BFF799D82CC03E61A65584D31D800924149453A`

I build solution in front and run `dotnet publish` with `--no-restore`, because otherwise I get an error:
`'artefacts\obj\ClientApp\project.assets.json' doesn't have a target for 'net6.0-windows10.0.19041.0'`

## Conventions

We use:
- [Conventional commits](https://www.conventionalcommits.org/en/v1.0.0/)
- Standard .NET naming & style guidelines. Exceptions:
    - Place `{` on the same line for `if`, `for`, and
      any other code inside method body
    - If you have to wrap a method body expression,
      wrap it before `=>` rather than after
    - A bunch of other things, see `#coding-style` Discord channel

## Release branches

Read [Nerdbank.GitVersioning docs](https://github.com/dotnet/Nerdbank.GitVersioning/blob/master/doc/nbgv-cli.md)  

```bash
# might not work, see https://github.com/dotnet/Nerdbank.GitVersioning/issues/685
nbgv prepare-release beta
```

We use the `alpha` suffix in the `master` branch, `beta`,`rc-*` in release branches. When a release branch drops the version suffix it becomes a production release.
