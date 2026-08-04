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
# Resolve to an absolute path now, while cwd is still the caller's — the script cds into
# $WORK below, and a repo-relative $SRC (as every Task 6-10 invocation passes) would no
# longer resolve after that.
SRC=$(cd "$(dirname "$SRC")" && pwd)/$(basename "$SRC")
REL=${SRC#"$REPO"/}

SDK=$(ls -d "$HOME"/.dotnet/sdk/11.0.* | tail -1)
CSC="$SDK/Roslyn/bincore/csc.dll"
BCLREF=$(ls -d "$HOME"/.dotnet/packs/Microsoft.NETCore.App.Ref/11.0.*/ref/net11.0 | tail -1)
TESTBIN=$REPO/artifacts/tests/bin/Chat.UI.Blazor.UnitTests/debug
# Chat.UI.Blazor.UnitTests doesn't reference Core.Audio, so ActualChat.Core.Audio.dll (e.g.
# PreRollBuffer) is missing from TESTBIN above - pull it from Core.Audio.UnitTests' own bin instead.
TESTBIN2=$REPO/artifacts/tests/bin/Core.Audio.UnitTests/debug

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
# webrtc-apm.dll is a native (non-managed) binary shipped alongside the test bin - csc rejects it.
[[ -d "$TESTBIN2" ]] && for f in "$TESTBIN2"/*.dll; do [[ "$(basename "$f")" == webrtc-apm.dll ]] || add "$f"; done

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
# NSErrorExt.Assert lives in the sibling "Maui" project (also Android/iOS/Windows-only, so it
# never lands in TESTBIN either); App.Maui's own GlobalUsings.cs opens ActualChat.Maui globally,
# which the synthesized GlobalUsings.cs above doesn't mirror - so both the using and the member
# are stubbed here.
cat > Stubs.cs <<'EOF'
global using System.Buffers;
global using ActualChat.Maui;
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
    public class BlazorWebViewApp
    {
        public IServiceProvider Services { get; } = null!;
        public static void EnsureStarted() { }
        public static Task<BlazorWebViewApp> WhenAppReady => Task.FromResult<BlazorWebViewApp>(null!);
    }
}
namespace ActualChat.App.Maui.Services
{
    public static class AppScopeAccessor
    {
        public static IServiceProvider? Current => null;
    }
    public class MauiNotifications
    {
        public Task RefreshNotificationToken(
            string token, ActualChat.Notifications.DeviceType deviceType, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
namespace ActualChat.Maui
{
    public static class NSErrorExt
    {
        public static void Assert(this Foundation.NSError? error, string? message = null) { }
    }
}
EOF

# The stubs below live in App.Maui files that reference (rather than define) each other, so each
# is only appended when the probed file itself isn't the one defining the real type - otherwise
# Current.cs's real definition collides with the stub (CS0101).
IOS_PUSH_TO_TALK_REL=src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs
AUDIO_SESSION_REL=src/dotnet/App.Maui/MaciOS/Audio/AudioSession.cs

if [[ "$REL" != "$IOS_PUSH_TO_TALK_REL" ]]; then
cat >> Stubs.cs <<'EOF'
namespace ActualChat.App.Maui
{
    public static class IosPushToTalk
    {
        public static void EnsureJoined() { }
        public static void Leave() { }
        public static void SetTransmitEnabled(bool isEnabled) { }
    }
}
EOF
fi

if [[ "$REL" != "$AUDIO_SESSION_REL" ]]; then
cat >> Stubs.cs <<'EOF'
namespace ActualChat.App.Maui.Audio
{
    public class AudioSession
    {
        public static ActualChat.UI.Blazor.Services.AudioSessionOwner Owner => default;
        public static void SetOwner(ActualChat.UI.Blazor.Services.AudioSessionOwner owner) { }
        public static void ReleaseOwner(
            ActualChat.UI.Blazor.Services.AudioSessionRelease release, bool hasLivePlayback = false) { }
        public Task EnsureCorrectOutputRoute() => Task.CompletedTask;
    }
}
EOF
fi

# AudioSession's owner watchdog and PttPreRoll's input-node guard both read AppleAudioCapture's
# process-level latch, so it's stubbed for every file but AppleAudioCapture.cs itself.
APPLE_AUDIO_CAPTURE_REL=src/dotnet/App.Maui/MaciOS/Audio/AppleAudioCapture.cs
if [[ "$REL" != "$APPLE_AUDIO_CAPTURE_REL" ]]; then
cat >> Stubs.cs <<'EOF'
namespace ActualChat.App.Maui.Audio
{
    public class AppleAudioCapture
    {
        public static bool IsInputNodeHeld => false;
    }
}
EOF
fi

# AppleAudioCapture.cs pulls in the AudioEngines/AudioEngine/InputNode/Resampler wrapper chain
# and IAudioCapture - none of them live in a built assembly TESTBIN(2) reaches, so they're
# stubbed here at the minimal surface AppleAudioCapture.cs actually calls. Skipped when the
# probed file is one of the types themselves, to avoid a definition collision (CS0101).
AUDIO_ENGINE_CLUSTER_RELS=(
    src/dotnet/App.Maui/Services/Recording/IAudioCapture.cs
    src/dotnet/App.Maui/MaciOS/Audio/ResamplerFactory.cs
    src/dotnet/App.Maui/MaciOS/Audio/Resampler.cs
    src/dotnet/App.Maui/MaciOS/Audio/AudioEngines.cs
    src/dotnet/App.Maui/MaciOS/Audio/EngineWrapper/AudioEngine.cs
    src/dotnet/App.Maui/MaciOS/Audio/EngineWrapper/InputNode.cs
    src/dotnet/App.Maui/MaciOS/Audio/EngineWrapper/AudioNode.cs
)
IS_AUDIO_ENGINE_CLUSTER_FILE=false
for rel in "${AUDIO_ENGINE_CLUSTER_RELS[@]}"; do
    [[ "$REL" == "$rel" ]] && IS_AUDIO_ENGINE_CLUSTER_FILE=true
done
AVAUDIO_PCM_BUFFER_EXT_REL=src/dotnet/App.Maui/MaciOS/Audio/AVAudioPcmBufferExt.cs
if [[ "$REL" != "$AVAUDIO_PCM_BUFFER_EXT_REL" ]]; then
cat >> Stubs.cs <<'EOF'
namespace ActualChat.App.Maui.Audio
{
    public static class AVAudioPcmBufferExt
    {
        public static unsafe ReadOnlySpan<float> AsReadOnlySpan(this AVFoundation.AVAudioPcmBuffer pcm) => default;
        public static void SetData(this AVFoundation.AVAudioPcmBuffer buffer, Span<float> data) { }
    }
}
EOF
fi

PTT_PRE_ROLL_REL=src/dotnet/App.Maui/MaciOS/Audio/PttPreRoll.cs
if [[ "$REL" != "$PTT_PRE_ROLL_REL" ]]; then
cat >> Stubs.cs <<'EOF'
namespace ActualChat.App.Maui.Audio
{
    public static class PttPreRoll
    {
        public static long Start() => 0;
        public static void Discard(long token) { }
        public static PreRollTake? TryTake() => null;
    }
    public sealed record PreRollTake(float[] Samples, AVFoundation.AVAudioFormat Format);
}
EOF
fi

if [[ "$IS_AUDIO_ENGINE_CLUSTER_FILE" == false ]]; then
cat >> Stubs.cs <<'EOF'
namespace ActualChat.App.Maui.Services.Recording
{
    public interface IAudioCapture
    {
        Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken);
    }
}
namespace ActualChat.App.Maui.Audio
{
    public class Resampler : IDisposable
    {
        public void Dispose() { }
        public void Transform(AVFoundation.AVAudioPcmBuffer input, BlockRingBuffer<float> output) { }
    }
    public class ResamplerFactory
    {
        public Resampler Create(AVFoundation.AVAudioFormat sourceFormat, AVFoundation.AVAudioFormat targetFormat) => new();
    }
    public class InputNode
    {
        public AVFoundation.AVAudioFormat GetOutputFormat() => null!;
        public void SetVoiceProcessingEnabled(bool value) { }
        public IDisposable Tap(AVFoundation.AVAudioNodeTapBlock callback) => null!;
    }
    public class AudioEngine
    {
        public static readonly AVFoundation.AVAudioFormat VoiceRecordingFormat = null!;
        public InputNode Input { get; } = null!;
        public void EnsureRunning() { }
    }
    public class AudioEngines
    {
        public AudioEngine Recording { get; } = null!;
    }
}
EOF
fi

if [[ "$REL" == "$IOS_PUSH_TO_TALK_REL" ]]; then
cat >> Stubs.cs <<'EOF'
namespace ActualChat.App.Maui.Services
{
    public abstract class WalkieTalkiePlatform
    {
        public abstract void OnWakeFailed(ChatId chatId);
        public abstract void OnHeadlessTeardown();
        public virtual (ChatId ChatId, Moment At)? LastWake => null;
        public virtual Task OnForegroundWakeHandled(ChatId chatId) => Task.CompletedTask;
    }
    public static class WalkieTalkieSession
    {
        public static Task HandleWake(ChatId chatId, Moment startedAt, bool isForeground, WalkieTalkiePlatform platform)
            => Task.CompletedTask;
        public static Task<ActualChat.UI.Blazor.App.Services.WalkieTalkieReply?> HandleTransmit(
            WalkieTalkiePlatform platform)
            => Task.FromResult<ActualChat.UI.Blazor.App.Services.WalkieTalkieReply?>(null);
    }
}
EOF
fi

WALKIE_TALKIE_SESSION_REL=src/dotnet/App.Maui/Services/WalkieTalkieSession.cs
if [[ "$REL" == "$WALKIE_TALKIE_SESSION_REL" ]]; then
cat >> Stubs.cs <<'EOF'
namespace ActualChat.App.Maui.Services
{
    public abstract class WalkieTalkiePlatform
    {
        public abstract void OnWakeFailed(ChatId chatId);
        public abstract void OnHeadlessTeardown();
        public virtual Task OnPlaybackStarted(
            ActualChat.UI.Blazor.App.Services.AppUIHub hub, ChatId chatId) => Task.CompletedTask;
        public virtual Task OnForegroundWakeHandled(ChatId chatId) => Task.CompletedTask;
    }
    public sealed class HeadlessBlazorScope : IAsyncDisposable
    {
        public static HeadlessBlazorScope? Current => null;
        public IServiceProvider Services => null!;
        public static HeadlessBlazorScope? GetOrCreate() => null;
        public static HeadlessBlazorScope? TryDetachCurrent(string reason) => null;
        public ValueTask DisposeAsync() => default;
    }
}
EOF
fi

compile() { # $1 = source file, $2 = out name, $3 = log
    dotnet exec "$CSC" -nostdlib -noconfig -target:library -nullable:enable \
        -langversion:preview -define:IOS -define:__IOS__ -unsafe \
        @refs.rsp GlobalUsings.cs Stubs.cs -out:"$2" "$1" > "$3" 2>&1
}

cp "$SRC" Current.cs
compile Current.cs current.dll current.log && echo "CURRENT: exit 0" || { echo "CURRENT: FAILED"; cat current.log; exit 1; }
echo "CURRENT errors: $(grep -c ' error ' current.log || true)"
echo "CURRENT warnings:"; grep -o 'warning CS[0-9]*' current.log | sort | uniq -c || true

if [[ -n "$BASELINE_REF" ]]; then
    git -C "$REPO" show "$BASELINE_REF:$REL" > Baseline.cs
    compile Baseline.cs baseline.dll baseline.log && echo "BASELINE($BASELINE_REF): exit 0" || { echo "BASELINE: FAILED"; cat baseline.log; exit 1; }
    echo "BASELINE errors: $(grep -c ' error ' baseline.log || true)"
fi
