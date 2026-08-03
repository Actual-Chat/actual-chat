#!/usr/bin/env bash
# Compiles a single App.Maui Android source file with `csc` alone, against the exact
# Mono.Android / AndroidX.Media / AndroidX.Core reference assemblies the project pins,
# plus the real freshly-built ActualChat.*/ActualLab.* closure from a test bin folder.
#
# Why this exists: App.Maui.csproj is outside ActualChat.CI.slnf and the Android SDK
# workload isn't installed on this machine, so `dotnet build` can't touch Android-only
# code here. This is the only thing on this machine that has ever compiled it. It caught
# real API/namespace mistakes during the walkie-talkie headset-button work (see
# .superpowers/sdd/2026-08-03-walkie-talkie-headset-button-e3/task-5-report.md).
#
# What it covers: the target file compiles against real reference assemblies, with a
# real global-usings set and small hand-written stubs for the handful of App.Maui-local
# types that aren't in any built assembly (edit SRC/STUBS below to point at a different
# file's dependency set).
#
# What it does NOT cover: Roslyn analyzers (CA1416/CA1422 stay silent under bare csc,
# confirmed by deliberately breaking a variant and seeing no diagnostic), the
# Microsoft.Maui/Microsoft.Maui.Controls global-using block that only applies inside
# App.Maui proper (an ambiguity from those namespaces would not show here — check by
# hand), and anything on-device (e.g. whether an earbud actually delivers a key event).
#
# This script is hand-tuned to one file's dependency set (imports, stubs, global usings).
# Pointing it at a different file will likely need edits to SRC, GlobalUsings.cs, and
# Stubs.cs below — it is a probe technique to copy, not a general-purpose compiler.
#
# Usage: scripts/csc-android-probe.sh [<git-ref-to-diff-against>]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
SRC=$REPO/src/dotnet/App.Maui/Platforms/Android/Audio/AndroidAudioWidgetForegroundService.cs
BASELINE_REF=${1:-}

SDK=$(ls -d /home/undead/.dotnet/sdk/11.0.* | tail -1)
CSC="$SDK/Roslyn/bincore/csc.dll"
BCLREF=$(ls -d /home/undead/.dotnet/packs/Microsoft.NETCore.App.Ref/11.0.*/ref/net11.0 | tail -1)
ANDREF=$(ls -d /home/undead/.dotnet/packs/Microsoft.Android.Ref.37/*/ref/net11.0 | tail -1)
MEDIA=/home/undead/.nuget/packages/xamarin.androidx.media/1.8.0/lib/net10.0-android36.0/Xamarin.AndroidX.Media.dll
CORE=/home/undead/.nuget/packages/xamarin.androidx.core/1.18.0/lib/net10.0-android36.0/Xamarin.AndroidX.Core.dll
# Any test bin dir works - it holds the freshly built, real ActualChat.*/ActualLab.* closure.
TESTBIN=$REPO/artifacts/tests/bin/Chat.UI.Blazor.UnitTests/debug

WORK=${WORK:-$REPO/tmp/csc-android-probe}
mkdir -p "$WORK"; cd "$WORK"

# --- reference set: ref packs win over the test bin on simple-name collisions ---
: > refs.rsp
declare -A seen
add() { local n; n=$(basename "$1" .dll); [[ -n "${seen[$n]:-}" ]] || { seen[$n]=1; echo "-r:$1" >> refs.rsp; }; }
for f in "$BCLREF"/*.dll; do add "$f"; done
add "$ANDREF/Mono.Android.dll"; add "$ANDREF/Java.Interop.dll"
add "$MEDIA"; add "$CORE"
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
namespace _Microsoft.Android.Resource.Designer
{
    public static class ResourceConstant
    {
        public static class Drawable { public const int notification_app_icon = 1; }
    }
}
namespace ActualChat.App.Maui
{
    public class AppServicesAccessor
    {
        public static void BeginDispatchToMainThread(Action action, bool allowInline = true) { }
    }
    public static class NotificationHelper
    {
        public static Task<Android.Graphics.Bitmap?> GetImageAsync(string imageUrl)
            => Task.FromResult<Android.Graphics.Bitmap?>(null);
        public static Android.Content.Intent? CreateViewIntent(Android.Content.Context context, string? link)
            => null;
    }
}
namespace ActualChat.App.Maui.Services
{
    public static class AppScopeAccessor
    {
        public static IServiceProvider? Current => null;
    }
}
namespace ActualChat.App.Maui.Audio
{
    public class AndroidAudioWidget
    {
        public static void Pause() { }
        public static void Resume() { }
        public static void Stop() { }
    }
}
EOF

compile() { # $1 = source file, $2 = out name, $3 = log
    dotnet exec "$CSC" -nostdlib -noconfig -target:library -nullable:enable \
        -langversion:preview -define:ANDROID -unsafe \
        @refs.rsp GlobalUsings.cs Stubs.cs -out:"$2" "$1" > "$3" 2>&1
}

cp "$SRC" Current.cs
compile Current.cs current.dll current.log && echo "CURRENT: exit 0" || { echo "CURRENT: FAILED"; cat current.log; exit 1; }
echo "CURRENT errors: $(grep -c ' error ' current.log || true)"
echo "CURRENT warnings:"; grep -o 'warning CS[0-9]*' current.log | sort | uniq -c

if [[ -n "$BASELINE_REF" ]]; then
    git -C "$REPO" show "$BASELINE_REF:src/dotnet/App.Maui/Platforms/Android/Audio/AndroidAudioWidgetForegroundService.cs" > Baseline.cs
    compile Baseline.cs baseline.dll baseline.log && echo "BASELINE($BASELINE_REF): exit 0" || { echo "BASELINE: FAILED"; cat baseline.log; exit 1; }
    echo "BASELINE errors: $(grep -c ' error ' baseline.log || true)"
    echo "BASELINE warnings:"; grep -o 'warning CS[0-9]*' baseline.log | sort | uniq -c
fi
