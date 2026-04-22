FROM mcr.microsoft.com/dotnet/aspnet:10.0.6 AS runtime
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
RUN sed -i 's|http://archive.ubuntu.com|https://archive.ubuntu.com|g' /etc/apt/sources.list.d/ubuntu.sources \
    && sed -i 's|http://security.ubuntu.com|https://security.ubuntu.com|g' /etc/apt/sources.list.d/ubuntu.sources \
    && apt update && apt install -y ffmpeg postgresql-client && apt clean
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0.202 AS dotnet-restore
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_CLI_UI_LANGUAGE=en-US \
    DOTNET_SVCUTIL_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    POWERSHELL_TELEMETRY_OPTOUT=1 \
    POWERSHELL_UPDATECHECK_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_ROLL_FORWARD=Major \
    DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 \
    NUGET_CERT_REVOCATION_MODE=offline

RUN sed -i 's|http://archive.ubuntu.com|https://archive.ubuntu.com|g' /etc/apt/sources.list.d/ubuntu.sources \
    && sed -i 's|http://security.ubuntu.com|https://security.ubuntu.com|g' /etc/apt/sources.list.d/ubuntu.sources \
    && apt update \
    && apt install -y --no-install-recommends python3 python3-pip libatomic1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY lib/ lib/
COPY nuget.config Directory.Build.* Directory.Packages.props .editorconfig ActualChat.sln ActualChat.Migrations.slnf ./
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
COPY run-build.cmd .

RUN dotnet workload install wasm-tools aspire \
    && ./run-build.cmd restore \
    && dotnet tool restore

# node:20-alpine because it's [cached on gh actions VM](https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2204-Readme.md#cached-docker-images)
FROM node:20-alpine AS nodejs-restore
ARG NPM_READ_TOKEN
ENV NPM_READ_TOKEN=$NPM_READ_TOKEN
WORKDIR /src
RUN apk update && apk add brotli gzip
RUN npm -g config set audit false && \
    npm -g config set audit-level critical && \
    npm -g config set fund false && \
    npm -g config set prefer-offline true && \
    npm -g config set progress false && \
    npm -g config set update-notifier false && \
    npm -g config set loglevel warn && \
    npm -g config set depth 0 && \
    apk add --no-cache git
COPY package-lock.json package.json .npmrc ./
RUN cat .npmrc && npm ci
COPY src/nodejs/ ./src/nodejs/
COPY build.mjs tsconfig.json tailwind.config.js postcss.config.mjs postcss-watch-plugin.js eslint.config.mjs ./

FROM scratch AS all-restore
COPY --from=nodejs-restore /src/package.json ./
COPY --from=dotnet-restore /src/nuget.config ./

FROM nodejs-restore AS nodejs-build
COPY src/dotnet/ /src/src/dotnet/
COPY resources/sounds/converted /src/resources/sounds/converted
RUN npm run build:Release

FROM dotnet-restore AS base
COPY src/dotnet/ src/dotnet/
COPY tests/ tests/
COPY *.props *.targets ./
# we need to regenerate ThisAssembly files with the new version info
RUN dotnet msbuild /t:GenerateAssemblyNBGVVersionInfo ActualChat.CI.slnf

FROM base AS dotnet-build
COPY --from=nodejs-build /src/src/dotnet/App.Wasm/wwwroot/ /src/src/dotnet/App.Wasm/wwwroot/
RUN dotnet publish --no-restore --nologo -c Release -nodeReuse:false -o /app ./src/dotnet/App.Server/App.Server.csproj

FROM dotnet-build AS migrations-build
COPY ./ef-migrations.cmd ./ef-migrations.cmd
# Migration projects declare <RuntimeIdentifier>linux-x64</RuntimeIdentifier>, so restore
# automatically adds net10.0/linux-x64 to project.assets.json and build outputs to
# artifacts/bin/<Proj>/debug_linux-x64/ — where `dotnet ef bundle --runtime linux-x64
# --no-build` looks for deps.json.
RUN dotnet restore ActualChat.Migrations.slnf
RUN dotnet build ActualChat.Migrations.slnf --no-restore -nodeReuse:false
# Bundle in parallel — each project writes to its own artifacts/obj/*, safe concurrently
RUN set -e; \
    pids=""; \
    for m in Chat.Service Contacts.Service Invite.Service Media.Service MLSearch.Service Notification.Service Users.Service; do \
      ./ef-migrations.cmd "$m" bundle --runtime linux-x64 --output "./artifacts/$m.Migration.exe" & \
      pids="$pids $!"; \
    done; \
    fail=0; for p in $pids; do wait "$p" || fail=1; done; \
    [ "$fail" = 0 ] && ls -lha /src/artifacts

FROM runtime AS migrations-app
COPY --from=migrations-build /src/artifacts/*.Migration.exe /migrations/
COPY <<"EOF" /migrations/entrypoint.sh
#!/bin/bash
./Chat.Service.Migration.exe --connection "Host=$HOST;Database=ac_${INSTANCE}chat;Port=$PORT;User Id=$USER;Password=$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True;Command Timeout=300"

./Contacts.Service.Migration.exe --connection "Host=$HOST;Database=ac_${INSTANCE}contacts;Port=$PORT;User Id=$USER;Password=$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True;Command Timeout=300"

./Flows.Service.Migration.exe --connection "Host=$HOST;Database=ac_${INSTANCE}flows;Port=$PORT;User Id=$USER;Password=$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True;Command Timeout=300"

./Invite.Service.Migration.exe --connection "Host=$HOST;Database=ac_${INSTANCE}invite;Port=$PORT;User Id=$USER;Password=$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True;Command Timeout=300"

./Media.Service.Migration.exe --connection "Host=$HOST;Database=ac_${INSTANCE}media;Port=$PORT;User Id=$USER;Password=$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True;Command Timeout=300"

./MLSearch.Service.Migration.exe --connection "Host=$HOST;Database=ac_${INSTANCE}mlsearch;Port=$PORT;User Id=$USER;Password=$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True;Command Timeout=300"

./Notification.Service.Migration.exe --connection "Host=$HOST;Database=ac_${INSTANCE}notification;Port=$PORT;User Id=$USER;Password=$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True;Command Timeout=300"

./Users.Service.Migration.exe --connection "Host=$HOST;Database=ac_${INSTANCE}users;Port=$PORT;User Id=$USER;Password=$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True;Command Timeout=300"
EOF
RUN chmod -R 755 /migrations/
WORKDIR /migrations
ENV HOST=localhost
ENV PORT=5432
ENV INSTANCE=dev_
ENV USER=postgres
ENV PASSWORD=postgres
ENTRYPOINT ["./entrypoint.sh"]

FROM runtime AS app
COPY --from=dotnet-build /app .
COPY --from=dotnet-build /src/.config/prompts /app/config/prompts
COPY --from=nodejs-build /src/src/dotnet/App.Wasm/wwwroot/ /app/wwwroot/
ENV ASPNETCORE_URLS=http://*:80
ENV CoreSettings__PromptsDir=/app/config/prompts
ENTRYPOINT ["dotnet", "ActualChat.App.Server.dll"]
