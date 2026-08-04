#!/usr/bin/env bash
# Compiles a single App.Maui iOS source file with `csc` alone, against the real Microsoft.iOS
# reference assembly, plus the freshly-built ActualChat.*/ActualLab.* closure from a test bin folder.
#
# Why this exists: App.Maui.csproj is outside ActualChat.CI.slnf AND net11.0-ios only builds on
# macOS, so `dotnet build` cannot touch iOS code here at all. This is the only thing on this
# machine that has ever compiled it. Sibling of scripts/csc-android-probe.sh.
#
# What it covers: the target file compiles against a real Microsoft.iOS ref assembly, so wrong
# API names, wrong overloads and wrong enum members are caught. It found three during E4 planning
# (FailedToBeginTransmittingInChannel's name, PTChannelTransmitRequestSource's member set, and
# AVAudioPcmBuffer.FloatChannelData being an nint rather than an indexable array).
#
# What it does NOT cover: Roslyn analyzers (CA1416 platform checks stay silent under bare csc),
# the Microsoft.Maui/Microsoft.Maui.Controls global-using block that only applies inside App.Maui
# proper, the native linker, and anything on-device.
#
# What it requires: a prior `dotnet build ActualChat.CI.slnf` (or a test run) for TESTBIN, and
# network access on first run to fetch the iOS ref pack into tmp/.
#
# The stub set below is hand-tuned per target file. Pointing it at a new file usually needs edits.
#
# Usage: scripts/csc-ios-probe.sh <source-file> [<git-ref-to-diff-against>]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
SRC=${1:?usage: csc-ios-probe.sh <source-file> [<baseline-git-ref>]}
BASELINE_REF=${2:-}
[[ -f "$SRC" ]] || { echo "No such file: $SRC"; exit 2; }
REL=${SRC#"$REPO"/}

SDK=$(ls -d "$HOME"/.dotnet/sdk/11.0.* | tail -1)
CSC="$SDK/Roslyn/bincore/csc.dll"
BCLREF=$(ls -d "$HOME"/.dotnet/packs/Microsoft.NETCore.App.Ref/11.0.*/ref/net11.0 | tail -1)
TESTBIN=$REPO/artifacts/tests/bin/Chat.UI.Blazor.UnitTests/debug

WORK=${WORK:-$REPO/tmp/csc-ios-probe}
mkdir -p "$WORK"; cd "$WORK"

# --- the iOS ref pack: a plain NuGet package, so it restores on Linux even though the
# --- net11.0-ios TFM itself only builds on macOS ---
IOSPKG=microsoft.ios.ref.net11.0_26.2
IOSVER=26.2.11588-net11-p3
IOSREF=$WORK/iosref/ref/net11.0/Microsoft.iOS.dll
if [[ ! -f "$IOSREF" ]]; then
    echo "Fetching $IOSPKG/$IOSVER ..."
    mkdir -p iosref
    curl -sSL --max-time 300 -o iosref/pkg.nupkg \
        "https://api.nuget.org/v3-flatcontainer/$IOSPKG/$IOSVER/$IOSPKG.$IOSVER.nupkg"
    python3 -c "import zipfile; zipfile.ZipFile('iosref/pkg.nupkg').extract('ref/net11.0/Microsoft.iOS.dll','iosref')"
fi

# --- reference set: the ref packs win over the test bin on simple-name collisions ---
: > refs.rsp
declare -A seen
add() { local n; n=$(basename "$1" .dll); [[ -n "${seen[$n]:-}" ]] || { seen[$n]=1; echo "-r:$1" >> refs.rsp; }; }
for f in "$BCLREF"/*.dll; do add "$f"; done
add "$IOSREF"
for f in "$TESTBIN"/*.dll; do add "$f"; done

# --- the project's global usings (root Directory.Build.props + App.Maui/Directory.Build.props) ---
cat > GlobalUsings.cs <<'EOF'
global using System;
global using System.Collections;
global using System.Collections.Concurrent;
global using System.Collections.Generic;
global using System.Collections.Immutable;
global using System.Diagnostics;
global using System.Diagnostics.CodeAnalysis;
global using System.Globalization;
global using System.Linq;
global using System.Reflection;
global using System.Runtime.CompilerServices;
global using System.Runtime.InteropServices;
global using System.Runtime.Serialization;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Threading;
global using System.Threading.Channels;
global using System.Threading.Tasks;
global using static System.FormattableString;
global using ActualChat;
global using ActualChat.Collections;
global using ActualChat.DependencyInjection;
global using ActualChat.Diff;
global using ActualChat.IO;
global using ActualChat.Mathematics;
global using ActualChat.Chat;
global using ActualChat.Media;
global using ActualChat.Users;
global using ActualChat.Performance;
global using ActualChat.Serialization;
global using ActualChat.Validation;
global using ActualLab;
global using ActualLab.Api;
global using ActualLab.Async;
global using ActualLab.Channels;
global using ActualLab.Collections;
global using ActualLab.Compliance;
global using ActualLab.DependencyInjection;
global using ActualLab.Mathematics;
global using ActualLab.Serialization;
global using ActualLab.OS;
global using ActualLab.Reflection;
global using ActualLab.Text;
global using ActualLab.Time;
global using ActualLab.Trimming;
global using ActualLab.Fusion;
global using ActualLab.Fusion.Operations;
global using ActualLab.CommandR;
global using ActualLab.CommandR.Configuration;
global using ActualLab.CommandR.Commands;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Logging.Abstractions;
global using Microsoft.Extensions.DependencyInjection;
global using static ActualChat.App.Maui.AppServicesAccessor;
EOF

# --- stubs, ONLY for App.Maui-local types that live in no built assembly ---
cat > Stubs.cs <<'EOF'
namespace ActualChat.App.Maui
{
    public class AppServicesAccessor
    {
        public static bool TryGetScopedServices(out IServiceProvider services)
        {
            services = null!;
            return false;
        }
        public static Task<T> DispatchToMainThread<T>(Func<T> func) => Task.FromResult(func());
        public static Task DispatchToMainThread(Action action) { action(); return Task.CompletedTask; }
    }
    public static class BlazorWebViewApp
    {
        public static void EnsureStarted() { }
        public static Task<IServiceProvider> WhenAppReady => Task.FromResult<IServiceProvider>(null!);
    }
    public static class IosPushToTalk
    {
        public static void EnsureJoined() { }
        public static void Leave() { }
    }
}
namespace ActualChat.App.Maui.Services
{
    public static class AppScopeAccessor
    {
        public static IServiceProvider? Current => null;
    }
}
EOF

compile() { # $1 = source file, $2 = out name, $3 = log
    dotnet exec "$CSC" -nostdlib -noconfig -target:library -nullable:enable \
        -langversion:preview -define:IOS -define:__IOS__ -unsafe \
        @refs.rsp GlobalUsings.cs Stubs.cs -out:"$2" "$1" > "$3" 2>&1
}

cp "$SRC" Current.cs
compile Current.cs current.dll current.log && echo "CURRENT: exit 0" || { echo "CURRENT: FAILED"; cat current.log; exit 1; }
echo "CURRENT errors: $(grep -c ' error ' current.log || true)"
echo "CURRENT warnings:"; grep -o 'warning CS[0-9]*' current.log | sort | uniq -c

if [[ -n "$BASELINE_REF" ]]; then
    git -C "$REPO" show "$BASELINE_REF:$REL" > Baseline.cs
    compile Baseline.cs baseline.dll baseline.log && echo "BASELINE($BASELINE_REF): exit 0" || { echo "BASELINE: FAILED"; cat baseline.log; exit 1; }
    echo "BASELINE errors: $(grep -c ' error ' baseline.log || true)"
fi
