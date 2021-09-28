FROM mcr.microsoft.com/dotnet/aspnet:6.0.0-rc.1-alpine3.14-amd64 as runtime
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_CLI_UI_LANGUAGE=en-US \
    DOTNET_SVCUTIL_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    POWERSHELL_TELEMETRY_OPTOUT=1 \
    POWERSHELL_UPDATECHECK_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
RUN apk add icu-libs --no-cache

# TODO: separate dotnet build image from webpack build image
FROM mcr.microsoft.com/dotnet/sdk:6.0.100-rc.1-alpine3.14-amd64 as base
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_CLI_UI_LANGUAGE=en-US \
    DOTNET_SVCUTIL_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    POWERSHELL_TELEMETRY_OPTOUT=1 \
    POWERSHELL_UPDATECHECK_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_CERT_REVOCATION_MODE=offline

WORKDIR /src
RUN apk add --no-cache icu-libs npm && \
    npm -g config set user root && \
    npm -g config set audit false && \
    npm -g config set audit-level critical && \
    npm -g config set fund false && \
    npm -g config set prefer-offline true && \
    npm -g config set progress false && \
    npm -g config set update-notifier false && \
    npm -g config set loglevel warn && \
    npm -g config set depth 0

COPY nuget.config Directory.Build.* Packages.props ActualChat.sln ./

# copy from {repoRoot}/src/dotnet/
COPY src/dotnet/*/*.csproj ./
RUN for file in $(ls *.csproj); do mkdir -p src/dotnet/${file%.*}/ && mv $file src/dotnet/${file%.*}/; done
COPY src/dotnet/Directory.Build.* src/dotnet/

# copy from {repoRoot}/tests/
COPY tests/*/*.csproj ./
RUN for file in $(ls *.csproj); do mkdir -p tests/${file%.*}/ && mv $file tests/${file%.*}/; done
COPY tests/Directory.Build.* tests/

RUN dotnet restore -nodeReuse:false

COPY ./ ./

FROM base as build
RUN dotnet publish --no-restore --nologo -c Release -nodeReuse:false -o /app ./src/dotnet/Host/Host.csproj

FROM runtime as app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ActualChat.Host.dll"]
