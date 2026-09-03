# Running Voxt

This guide covers setting up and running Voxt for local development.

## Prerequisites

- .NET 11 SDK (preview — see `global.json`)
- Node.js 20+
- Docker (for PostgreSQL, Redis, NATS)
- Visual Studio 2022 or JetBrains Rider (recommended)

## Initial Setup

### 1. Clone and Restore

```bash
git clone https://github.com/ActualChat/ActualChat.git
cd ActualChat

# Restore dependencies
dotnet restore
dotnet tool restore
./npm-install.cmd
```

### 2. Set Up Local Environment

Configure local development secrets:

**Windows:**
```bash
set-local-env.cmd
```

**macOS/Linux:**
```bash
./set-local-env.sh
```

> **Note:** Restart IDEs and shells to pick up new environment variables.

> **Important:** You'll need access to the internal credentials document (stored in our internal documentation) to configure all required secrets for running Voxt locally.

### 3. Start Infrastructure

Start the required services (PostgreSQL, Redis, NATS):

```bash
docker compose up -d --build --wait
```

> Most of what follows is also available through [`b`](./build-tool.md), the
> repo's build tool — `b server run`, `b app run android`, `b unit-tests` and so
> on. Run `b` with no arguments for an interactive menu, or `b tree -o` for the
> full command list.

## Building

Use the CI solution filter to exclude MAUI projects (unless you have MAUI workloads installed):

```bash
# Build all (excluding MAUI)
dotnet build ActualChat.CI.slnf

# Build specific project
dotnet build src/dotnet/App.Server/App.Server.csproj
```

## Running the Server

```bash
# Start the server
dotnet run --project src/dotnet/App.Server

# Or use the watch mode for development
./dotnet-watch.cmd
```

The server will be available at https://local.voxt.ai (or https://localhost:5001).

## Running Tests

```bash
# Ensure infrastructure is running
docker compose up -d --build --wait

# Run all tests
dotnet test ActualChat.CI.slnf

# Run specific test project
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj
```

For detailed information on test accounts, Playwright, and browser automation, see the [Testing Guide](/testing/overview).

## Running Documentation Site

The documentation uses VitePress.

```bash
cd docs

# Install dependencies (first time only)
npm install

# Serve locally with hot reload
./Run-Docs.cmd
```

The site will be available at http://localhost:5173.

To build the static site:

```bash
cd docs
./Build-Site.cmd
```

Output will be in `docs/.vitepress/dist/`.

## Mobile Development

### iOS Development on Windows

#### Prerequisites

1. Visual Studio 2022 with MAUI workload
2. MAUI workload: `dotnet workload install maui`
3. iTunes installed
4. Physical iPhone connected via USB

#### Steps

1. Open `ActualChat.sln` in Visual Studio
2. Select **iOS Local Device** as the target
3. Ensure **Debug** configuration and **App.Maui** project are selected
4. Press **Start Debugging**
5. Follow the first-time setup wizard
6. Connect your iPhone and trust the computer when prompted
7. Sign in with your Apple ID (Enterprise account)

#### Troubleshooting

**GetBuildVersion fails:**
Restart Visual Studio (sometimes requires double restart).

**Could not find an app content:**
Shorten `TEMP` and `TMP` environment variables (e.g., set to `C:\tmp`).

**Path too long errors:**
Shorten `AssemblyName` in `App.Maui.csproj`:
```xml
<AssemblyName>ac</AssemblyName>
```

Logs are located at `%LOCALAPPDATA%\Xamarin\Logs\17.0\*.Ide.log`.

### iOS Safari Debugging

To debug the web app on iOS Safari:

1. Ensure `local.voxt.ai` is configured and running
2. Run `./add-hosts.cmd` to configure hosts file
3. On iOS, install the development certificate from `.config/local.voxt.ai/ssl/`
4. Configure iOS DNS settings to point to your development machine

## Publishing

> **Note:** All packages are currently deployed automatically via GitHub Actions after approval. The commands below are for manual builds when needed.

### Android

```bash
cd src/dotnet/App.Maui

# Production build
dotnet publish -f:net11.0-android -c:Release /p:IsDevMaui=false

# Development build
dotnet publish -f:net11.0-android -c:Release /p:IsDevMaui=true
```

- `IsDevMaui=false`: Production package (`actual.chat.app`) connecting to https://actual.chat
- `IsDevMaui=true`: Development package (`actual.chat.dev.app`) connecting to https://dev.actual.chat

### Windows Store

```bash
./run-build.cmd pack-win --configuration Release
```

The package will be in `artifacts/publish/App.Maui/release_net11.0-windows10.0.22621.0_win-x64/`.

## Infrastructure Access

### Connecting to Databases

**Development AlloyDB:**
```bash
./connect-alloydb-dev.sh
# Connect to localhost:5433
```

**Production AlloyDB:**
```bash
./connect-alloydb-prod.sh
# Connect to localhost:5434
```

### Kubernetes Access

**Development cluster:**
```bash
./k8s.connect-dev.sh
```

**Production cluster (via bastion):**
```bash
gcloud auth login
gcloud compute ssh "bastion-host-prod-1" --project "actual-chat-app-prod" \
  --zone "us-central1-a" --tunnel-through-iap -- -L 8888:localhost:8888 -N -q -f

# Use kubectl through proxy
HTTPS_PROXY=localhost:8888 kubectl get pods
```

### Downloading Diagnostics

```bash
# Connect to cluster first
./k8s.connect-dev.sh

# Download dump
./k8s.dump.sh              # Mini dump
./k8s.dump.sh Full         # Full dump
./k8s.dump.sh WithHeap     # Dump with heap
```

Dumps are uploaded to GCS bucket (link shown after completion).

## OpenSearch

**Development dashboard:** https://dashboards-dev.actual.chat/

### Installation from Scratch

1. Run Terraform `tf-team-core` 01 to deploy search nodes
2. Apply Helm charts with Flux CD
3. Run Terraform `tf-team-core` 03 to deploy load balancer
4. Apply configuration with `securityadmin.sh`:
   ```bash
   kubectl exec -it opensearch-cluster-master-0 -- /bin/bash
   cd /usr/share/opensearch/plugins/opensearch-security/tools
   ./securityadmin.sh -h opensearch-cluster-master \
     -cd ../../../config/opensearch-security/ \
     -cacert ../../../config/admin/ca.crt \
     -cert ../../../config/admin/tls.crt \
     -key ../../../config/admin/tls.key
   ```
