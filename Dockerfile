FROM mcr.microsoft.com/dotnet/aspnet:6.0.3-bullseye-slim-amd64 as runtime
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_CLI_UI_LANGUAGE=en-US \
    DOTNET_SVCUTIL_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    POWERSHELL_TELEMETRY_OPTOUT=1 \
    POWERSHELL_UPDATECHECK_OPTOUT=1 \
    DOTNET_ROLL_FORWARD=Major \
    DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0.201-bullseye-slim-amd64 as dotnet-restore
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_CLI_UI_LANGUAGE=en-US \
    DOTNET_SVCUTIL_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    POWERSHELL_TELEMETRY_OPTOUT=1 \
    POWERSHELL_UPDATECHECK_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
# workaround of https://github.com/microsoft/playwright-dotnet/issues/1791
    DOTNET_ROLL_FORWARD=Major \
    DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 \
    NUGET_CERT_REVOCATION_MODE=offline

WORKDIR /src
COPY lib/ lib/
COPY nuget.config Directory.Build.* Packages.props .editorconfig ActualChat.sln ./
COPY .config/ .config/
# copy from {repoRoot}/src/dotnet/
COPY src/dotnet/*/*.csproj ./
RUN for file in $(ls *.csproj); do mkdir -p src/dotnet/${file%.*}/ && mv $file src/dotnet/${file%.*}/; done
COPY src/dotnet/Directory.Build.* src/dotnet/tsconfig.json src/dotnet/

# copy from {repoRoot}/tests/
COPY tests/*/*.csproj ./
RUN for file in $(ls *.csproj); do mkdir -p tests/${file%.*}/ && mv $file tests/${file%.*}/; done
COPY tests/Directory.Build.* tests/.editorconfig tests/

COPY build/ build/
RUN dotnet run --project build --configuration Release -- restore restore-tools

# node:16-alpine because it's [cached on gh actions VM](https://github.com/actions/virtual-environments/blob/main/images/linux/Ubuntu2004-Readme.md#cached-docker-images)
FROM node:16-alpine as nodejs-restore
ARG GITHUB_TOKEN
ENV GITHUB_TOKEN=$GITHUB_TOKEN
WORKDIR /src/src/nodejs
RUN echo $GITHUB_TOKEN && \
    cat .npmrc && \
    npm -g config set user root && \
    npm -g config set audit false && \
    npm -g config set audit-level critical && \
    npm -g config set fund false && \
    npm -g config set prefer-offline true && \
    npm -g config set progress false && \
    npm -g config set update-notifier false && \
    npm -g config set loglevel warn && \
    npm -g config set depth 0 && \
    apk add --no-cache git
COPY src/nodejs/package-lock.json src/nodejs/package.json src/nodejs/.npmrc ./
RUN npm ci
COPY src/nodejs/ ./

FROM nodejs-restore as nodejs-build
COPY src/dotnet/ /src/src/dotnet/
RUN npm run build:Release

FROM dotnet-restore as base
COPY src/dotnet/ src/dotnet/
COPY tests/ tests/
COPY *.props *.targets ./
# we need to regenerate ThisAssembly files with the new version info
RUN dotnet msbuild /t:GenerateAssemblyVersionInfo ActualChat.sln

FROM base as dotnet-build
RUN apt update \
    && apt install -y --no-install-recommends python3 python3-pip \
    && rm -rf /var/lib/apt/lists/*
RUN dotnet publish --no-restore --nologo -c Release -nodeReuse:false -o /app ./src/dotnet/Host/Host.csproj

FROM runtime as app
COPY --from=dotnet-build /app .
COPY --from=nodejs-build /src/src/dotnet/UI.Blazor.Host/wwwroot/ /app/wwwroot/
ENTRYPOINT ["dotnet", "ActualChat.Host.dll"]
