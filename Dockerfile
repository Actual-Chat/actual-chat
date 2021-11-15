FROM mcr.microsoft.com/dotnet/aspnet:6.0.0-alpine3.14-amd64 as runtime
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
RUN apk add icu-libs --no-cache
# install glibc for protoc
RUN apk add --no-cache wget libc6-compat && \
    wget -q -O /etc/apk/keys/sgerrand.rsa.pub https://alpine-pkgs.sgerrand.com/sgerrand.rsa.pub && \
    wget https://github.com/sgerrand/alpine-pkg-glibc/releases/download/2.33-r0/glibc-2.33-r0.apk && \
    apk add glibc-2.33-r0.apk && \
    rm glibc-2.33-r0.apk

FROM mcr.microsoft.com/dotnet/sdk:6.0.100-alpine3.14-amd64 as dotnet-restore
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
# install glibc for protoc
RUN apk add --no-cache wget libc6-compat && \
    wget -q -O /etc/apk/keys/sgerrand.rsa.pub https://alpine-pkgs.sgerrand.com/sgerrand.rsa.pub && \
    wget https://github.com/sgerrand/alpine-pkg-glibc/releases/download/2.33-r0/glibc-2.33-r0.apk && \
    apk add glibc-2.33-r0.apk && \
    rm glibc-2.33-r0.apk
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

# node:14-alpine because it's cached on gh actions VM
FROM node:14-alpine as nodejs-restore
WORKDIR /src/src/nodejs
RUN npm -g config set user root && \
    npm -g config set audit false && \
    npm -g config set audit-level critical && \
    npm -g config set fund false && \
    npm -g config set prefer-offline true && \
    npm -g config set progress false && \
    npm -g config set update-notifier false && \
    npm -g config set loglevel warn && \
    npm -g config set depth 0 && \
    apk add --no-cache git
COPY src/nodejs/package-lock.json src/nodejs/package.json ./
RUN npm ci
COPY src/nodejs/ ./

FROM nodejs-restore as nodejs-build
COPY src/dotnet/ /src/src/dotnet/
RUN npm run build:Release

FROM dotnet-restore as base
COPY src/dotnet/ src/dotnet/
COPY tests/ tests/

FROM base as dotnet-build
RUN dotnet publish --no-restore --nologo -c Release -nodeReuse:false -o /app ./src/dotnet/Host/Host.csproj

FROM runtime as app
COPY --from=dotnet-build /app .
COPY --from=nodejs-build /src/src/dotnet/UI.Blazor.Host/wwwroot/ /app/wwwroot/
ENTRYPOINT ["dotnet", "ActualChat.Host.dll"]
