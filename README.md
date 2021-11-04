# Actual Chat




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


## Our project/services conventions

* The contracts and implementations of the services which should be accessible from frontend (wasm) and backend (separate microservice) should be placed in `{Module}.csproj`. 
* `{Module}.Service.csproj` - might be ran by the host as a separate microservice (later). Should contain the general implementations of the services. For example: `AuthorService : IAuthorService`, shouldn't contain any `Session` and other frontend stuff. `IAuthorService` should be internal, because this service mustn't be accessed by the code of another module (microservice).
* For calling we create 2 separate public facades for the `IAuthorService` one of them placed in
    * `{Module}.Backend.Contracts.csproj` - for the `IAuthorServiceBackend` facade. (is used in internal network calls between microservices)
    * `{Module}.Frontend.Contracts.csproj` - for the `IAuthorServiceFronend` facade. (is used between client and service (wasm -> public controller which visible from the internet))
* `{Module}.Backend.Client.csproj` contains rest client to the internal part of service (controllers under `/internal/` url) (`IAuthorServiceBackendDef` files)
* `{Module}.Frontend.Client.csproj` contains rest client to the external part of service (public visible controllers) (`IAuthorServiceFrontendDef` files)