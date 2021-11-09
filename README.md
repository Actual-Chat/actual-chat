# Actual Chat

![unit tests](https://github.com/Actual-Chat/actual-chat/actions/workflows/unit-tests.yml/badge.svg)




## Prerequisites

- [Git](https://git-scm.com/downloads)
- [.NET 6 RC2](https://dotnet.microsoft.com/download/dotnet/6.0)
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
nbgv prepare-release beta
```

We use the `alpha` suffix in the `master` branch, `beta`,`rc-*` in release branches.  
When a release branch drops the version suffix it becomes a production release.
