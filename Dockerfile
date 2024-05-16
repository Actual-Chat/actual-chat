FROM mcr.microsoft.com/dotnet/aspnet:8.0.5-bookworm-slim as runtime
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
RUN apt update && apt install -y ffmpeg && apt clean
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0.300-bookworm-slim as dotnet-restore
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

RUN apt update \
    && apt install -y --no-install-recommends python3 python3-pip libatomic1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY lib/ lib/
COPY nuget.config Directory.Build.* Directory.Packages.props .editorconfig ActualChat.sln ./
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

RUN ./run-build.cmd restore \
    && dotnet workload install wasm-tools

# node:20-alpine because it's [cached on gh actions VM](https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2204-Readme.md#cached-docker-images)
FROM node:20-alpine as nodejs-restore
ARG NPM_READ_TOKEN
ENV NPM_READ_TOKEN=$NPM_READ_TOKEN
WORKDIR /src/src/nodejs
RUN apk update && apk add brotli gzip
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
COPY src/nodejs/package-lock.json src/nodejs/package.json src/nodejs/.npmrc ./
RUN cat .npmrc && npm ci
COPY src/nodejs/ ./

FROM scratch as all-restore
COPY --from=nodejs-restore /src/src/nodejs/package.json ./
COPY --from=dotnet-restore /src/nuget.config ./

FROM nodejs-restore as nodejs-build
COPY src/dotnet/ /src/src/dotnet/
RUN npm run build:Release \
    && find /src/src/dotnet/App.Wasm/wwwroot/dist/wasm/*.wasm -print | xargs gzip -k9 \
    && find /src/src/dotnet/App.Wasm/wwwroot/dist/wasm/*.wasm -print | xargs brotli -Z \
    && find /src/src/dotnet/App.Wasm/wwwroot/dist/*.js -print | xargs gzip -k9 \
    && find /src/src/dotnet/App.Wasm/wwwroot/dist/*.js -print | xargs brotli -Z \
    && find /src/src/dotnet/App.Wasm/wwwroot/dist/*.css -print | xargs gzip -k9 \
    && find /src/src/dotnet/App.Wasm/wwwroot/dist/*.css -print | xargs brotli -Z

FROM dotnet-restore as base
COPY src/dotnet/ src/dotnet/
COPY tests/ tests/
COPY *.props *.targets ./
# we need to regenerate ThisAssembly files with the new version info
RUN dotnet msbuild /t:GenerateAssemblyNBGVVersionInfo ActualChat.CI.slnf

FROM base as dotnet-build
RUN dotnet publish --no-restore --nologo -c Release -nodeReuse:false -o /app ./src/dotnet/App.Server/App.Server.csproj

FROM dotnet-build as migrations-build
COPY ./ef-migrations.cmd ./ef-migrations.cmd
RUN dotnet tool restore
RUN dotnet build --runtime linux-x64 src/dotnet/Chat.Service.Migration/Chat.Service.Migration.csproj \
    && dotnet build --runtime linux-x64 src/dotnet/Contacts.Service.Migration/Contacts.Service.Migration.csproj \
    && dotnet build --runtime linux-x64 src/dotnet/Invite.Service.Migration/Invite.Service.Migration.csproj \
    && dotnet build --runtime linux-x64 src/dotnet/Media.Service.Migration/Media.Service.Migration.csproj \
    && dotnet build --runtime linux-x64 src/dotnet/MLSearch.Service.Migration/MLSearch.Service.Migration.csproj \
    && dotnet build --runtime linux-x64 src/dotnet/Notification.Service.Migration/Notification.Service.Migration.csproj \
    && dotnet build --runtime linux-x64 src/dotnet/Search.Service.Migration/Search.Service.Migration.csproj \
    && dotnet build --runtime linux-x64 src/dotnet/Users.Service.Migration/Users.Service.Migration.csproj
RUN ./ef-migrations.cmd Chat.Service bundle --runtime linux-x64 --output ./artifacts/Chat.Service.Migration.exe \
    && ./ef-migrations.cmd Contacts.Service bundle --runtime linux-x64 --output ./artifacts/Contacts.Service.Migration.exe \
    && ./ef-migrations.cmd Invite.Service bundle --runtime linux-x64 --output ./artifacts/Invite.Service.Migration.exe \
    && ./ef-migrations.cmd Media.Service bundle --runtime linux-x64 --output ./artifacts/Media.Service.Migration.exe \
    && ./ef-migrations.cmd MLSearch.Service bundle --runtime linux-x64 --output ./artifacts/MLSearch.Service.Migration.exe \
    && ./ef-migrations.cmd Notification.Service bundle --runtime linux-x64 --output ./artifacts/Notification.Service.Migration.exe \
    && ./ef-migrations.cmd Search.Service bundle --runtime linux-x64 --output ./artifacts/Search.Service.Migration.exe \
    && ./ef-migrations.cmd Users.Service bundle --runtime linux-x64 --output ./artifacts/Users.Service.Migration.exe \
    && ls -lha /src/artifacts

FROM runtime as migrations-app
COPY --from=migrations-build /src/artifacts/*.Migration.exe /migrations/
COPY <<EOF /migrations/entrypoint.sh
#!/bin/bash
./Chat.Service.Migration.exe --connection "Host=\$HOST;Database=ac_\${INSTANCE}chat;Port=\$PORT;User Id=\$USER;Password=\$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True"
./Contacts.Service.Migration.exe --connection "Host=\$HOST;Database=ac_\${INSTANCE}contacts;Port=\$PORT;User Id=\$USER;Password=\$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True"
./Invite.Service.Migration.exe --connection "Host=\$HOST;Database=ac_\${INSTANCE}invite;Port=\$PORT;User Id=\$USER;Password=\$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True"
./Media.Service.Migration.exe --connection "Host=\$HOST;Database=ac_\${INSTANCE}media;Port=\$PORT;User Id=\$USER;Password=\$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True"
./MLSearch.Service.Migration.exe --connection "Host=\$HOST;Database=ac_\${INSTANCE}mlsearch;Port=\$PORT;User Id=\$USER;Password=\$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True"
./Notification.Service.Migration.exe --connection "Host=\$HOST;Database=ac_\${INSTANCE}notification;Port=\$PORT;User Id=\$USER;Password=\$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True"
./Search.Service.Migration.exe --connection "Host=\$HOST;Database=ac_\${INSTANCE}search;Port=\$PORT;User Id=\$USER;Password=\$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True"
./Users.Service.Migration.exe --connection "Host=\$HOST;Database=ac_\${INSTANCE}users;Port=\$PORT;User Id=\$USER;Password=\$PASSWORD;Enlist=false;Minimum Pool Size=1;Maximum Pool Size=100;Connection Idle Lifetime=30;Max Auto Prepare=8;Include Error Detail=True"
EOF
RUN chmod -R 755 /migrations/
WORKDIR /migrations
ENV HOST=localhost
ENV PORT=5432
ENV INSTANCE=dev_
ENV USER=postgres
ENV PASSWORD=postgres
ENTRYPOINT ["./entrypoint.sh"]

FROM runtime as app
COPY --from=dotnet-build /app .
COPY --from=nodejs-build /src/src/dotnet/App.Wasm/wwwroot/ /app/wwwroot/
ENV ASPNETCORE_URLS=http://*:80
ENTRYPOINT ["dotnet", "ActualChat.App.Server.dll"]
