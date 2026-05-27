# Video warmup → openGate: C# bridge

## Context

Predecessor work (commits `8eab9fac7` → `b548cbb57` on `dev`) added the
TS-side machinery for a warmup recording pipeline:

- `VideoRecorder.warmup(chatId, audienceCodecs)` — starts the real
  encoder pipeline with the wire-gate closed (output discarded). 1-tier
  ladder at top resolution.
- `VideoRecorder.openGate(maxLayerCount)` — flips the gate open,
  reconfigures the ladder to full simulcast, forces a keyframe, fires
  `OnRecordingStarted`.
- `VideoRecorder.cancelWarmup()` — tear down without firing the
  started-callback.
- Synthetic OffscreenCanvas encoder probe gutted; pre-flight is
  `VideoEncoder.isConfigSupported` only.

Missing piece: **no C# code calls warmup/openGate/cancelWarmup yet.**
`ChatVideoUI.StateSync` still calls `recorder.StartRecording(...)`
end-to-end whenever a `CameraRecordingIntent` arrives. The modal still
opens a separate preview session and submit-closes before any
recorder is created. That preserves the bug the warmup design was
meant to fix (cold-start latency + HW-slot churn between modal close
and stream start; per-codec failures still surface only at recording
time, not earlier).

This plan bridges the gap.

## Target flow

```
Modal opens
  ├─ existing preview pipeline starts (LocalVideoSession / RecorderVideoSession)
  └─ ChatVideoUI.StartWarmupForChat(chatId, audienceCodecs)
        └─ creates JS VideoRecorder, calls warmup() — gate closed, 1 tier

User clicks Start
  ├─ modal closes with IsConfirmed=true (warmup recorder stays alive)
  └─ caller fires CameraRecordingIntent

StateSync sees intent
  ├─ recorder = ChatVideoUI.ClaimWarmupRecorderForIntent(intent) ?? VideoRecorder.Create(...)
  └─ if recorder was the warmup one → recorder.OpenGate(maxLayerCount)
     else → recorder.StartRecording(chatId, ...)

User cancels modal (no submit)
  └─ ChatVideoUI.CancelWarmupForChat(chatId)
        └─ recorder.CancelWarmup() + dispose
```

## Reuse

**Existing TS-side:** `warmup`, `openGate`, `cancelWarmup` on `VideoRecorder`
JS. RPC contract `RecorderWorker.setGateOpen` already wired through
`recorder-worker-contract.ts`.

**Existing C# infrastructure:**
- `VideoRecorder.cs` — wraps the JS VideoRecorder. Has `StartRecording`,
  `StopRecording`, `SwitchCamera`, `ToggleBlur`, `SetLayers`.
  Will gain `Warmup`, `OpenGate`, `CancelWarmup`.
- `ChatVideoUI` — owns the recorder lifecycle today. Will gain a
  `_warmupRecorderByKind` field.
- `ChatVideoUI.StateSync.cs` — `StartCamera(recorder, intent, ct)` already
  has the right shape; just needs branch on warmup state.
- `RecorderCallbacks.OnRecordingError` — already plumbed for failure
  reporting. Warmup adds runtime codec-fallback (Phase 5).

No new abstractions; no candidates for `Core` promotion. All changes
are in `UI.Blazor.App.Services` and one Razor file.

## Phases

### Phase 1 — C# `VideoRecorder.cs` API surface

`src/dotnet/UI.Blazor.App/Services/VideoRecorder.cs`:

```csharp
public async Task Warmup(ChatId chatId, CancellationToken cancellationToken)
{
    if (_startRequest.HasValue)
        throw StandardError.Constraint("Start request already set");
    _startRequest = (chatId, true);  // (chatId, isCamera=true)
    var codecs = await GetInitialAudienceCodecs(chatId).ConfigureAwait(false);
    await _jsRef
        .InvokeVoidAsync("warmup", cancellationToken, chatId.Value, codecs)
        .ConfigureAwait(false);
}

public Task OpenGate(int maxLayerCount, CancellationToken cancellationToken)
    => _jsRef.InvokeVoidAsync("openGate", cancellationToken, maxLayerCount).AsTask();

public Task CancelWarmup(CancellationToken cancellationToken)
    => _jsRef.InvokeVoidAsync("cancelWarmup", cancellationToken).AsTask();
```

Notes:
- `_startRequest` is set by `Warmup` so `RunMaintenance` (which currently
  awaits `_whenStartedTaskCompletionSource`) doesn't fire prematurely.
  `OnRecordingStarted` is invoked by JS only after `openGate` — so
  `RunMaintenance`'s subscriptions begin at the right moment.
- Camera/blur APIs (`SwitchCamera`, `ToggleBlur`) already work on the
  warmup recorder unchanged — JS-side methods don't care about gate
  state.

### Phase 2 — ChatVideoUI warmup ownership

`src/dotnet/UI.Blazor.App/Services/ChatVideoUI.cs` (or a new
partial `ChatVideoUI.Warmup.cs`):

```csharp
private readonly object _warmupLock = new();
private VideoRecorder? _cameraWarmupRecorder;
private ChatId _cameraWarmupChatId;

public async Task<bool> StartCameraWarmup(ChatId chatId, CancellationToken ct)
{
    lock (_warmupLock) {
        if (_cameraWarmupChatId == chatId && _cameraWarmupRecorder is not null)
            return true; // idempotent
        if (_cameraWarmupRecorder is not null)
            return false; // warmup for different chat — caller must cancel first
    }
    var recorder = await VideoRecorder.Create(Hub, VideoSourceKind.Camera).ConfigureAwait(false);
    lock (_warmupLock) {
        if (_cameraWarmupRecorder is not null) {
            // race: someone else won
            _ = recorder.DisposeAsync();
            return false;
        }
        _cameraWarmupRecorder = recorder;
        _cameraWarmupChatId = chatId;
    }
    await recorder.Warmup(chatId, ct).ConfigureAwait(false);
    return true;
}

public async Task CancelCameraWarmup(ChatId chatId)
{
    VideoRecorder? toDispose;
    lock (_warmupLock) {
        if (_cameraWarmupRecorder is null || _cameraWarmupChatId != chatId)
            return;
        toDispose = _cameraWarmupRecorder;
        _cameraWarmupRecorder = null;
        _cameraWarmupChatId = default;
    }
    try { await toDispose.CancelWarmup(CancellationToken.None); }
    catch { /* best-effort */ }
    await toDispose.DisposeAsync();
}

// Called by StateSync when an intent fires.
internal VideoRecorder? TryClaimWarmupRecorder(ChatId chatId, VideoSourceKind kind)
{
    if (kind != VideoSourceKind.Camera) return null;
    lock (_warmupLock) {
        if (_cameraWarmupRecorder is null || _cameraWarmupChatId != chatId)
            return null;
        var claimed = _cameraWarmupRecorder;
        _cameraWarmupRecorder = null;
        _cameraWarmupChatId = default;
        return claimed;
    }
}
```

Screen-cast warmup deferred — the screencast track is acquired via
user gesture in `startScreenCast`, so warmup before that gesture isn't
viable. Camera-only for now.

### Phase 3 — StateSync claims the warmup recorder

`src/dotnet/UI.Blazor.App/Services/ChatVideoUI.StateSync.cs`:

Today's flow: when intent fires, `RecordingIntent` is materialized →
`VideoRecorder.Create` → `StartCamera(recorder, intent, ct)` → recorder
runs.

New flow: factor recorder acquisition behind a helper.

```csharp
private async Task<VideoRecorder> AcquireRecorder(RecordingIntent intent, CancellationToken ct)
{
    if (intent is CameraRecordingIntent cam) {
        var claimed = Hub.ChatVideoUI.TryClaimWarmupRecorder(cam.ChatId, VideoSourceKind.Camera);
        if (claimed is not null) {
            // Apply per-intent settings to the warm recorder before opening the gate.
            await claimed.SetSelectedCamera(cam.CameraDeviceId ?? "", ct).ConfigureAwait(false);
            await claimed.SetBlurEnabled(cam.BlurEnabled, ct).ConfigureAwait(false);
            return claimed;
        }
    }
    return await VideoRecorder.Create(Hub, intent is ScreenCastIntent
        ? VideoSourceKind.Screen
        : VideoSourceKind.Camera).ConfigureAwait(false);
}

private async Task StartCamera(VideoRecorder recorder, CameraRecordingIntent intent, CancellationToken ct)
{
    if (recorder.IsWarmedUp) {  // new C# property; true after Warmup, false after StartRecording
        await recorder
            .OpenGate(Hub.VideoQualityUI.OutboundDeviceCameraCap, ct)
            .ConfigureAwait(false);
    }
    else {
        await recorder.SetSelectedCamera(intent.CameraDeviceId ?? "", ct).ConfigureAwait(false);
        await recorder.SetBlurEnabled(intent.BlurEnabled, ct).ConfigureAwait(false);
        await recorder
            .StartRecording(intent.ChatId, Hub.VideoQualityUI.OutboundDeviceCameraCap, ct)
            .ConfigureAwait(false);
    }
}
```

`VideoRecorder.IsWarmedUp` lives on the C# class — flipped to true by
`Warmup` and back to false on `StopRecording`/`OpenGate` resolution.

### Phase 4 — JoinVideoCallModal lifecycle hooks

`src/dotnet/UI.Blazor.App/Components/JoinVideoCallModal/JoinVideoCallModal.razor`:

1. **Drop** `probeEncoderSupport` block (`ProbeEncoderSupport` background
   task, `_encoderUnavailable` flag, `EncoderUnavailableMessage` constant,
   the disabled-Submit-due-to-encoder check). The synthetic probe is gone.

2. **Add** warmup kickoff alongside `StartPreview` in `InitializeCamera`:

   ```csharp
   if (!IsSettingsMode && ModalModel.Chat?.Id is { } chatId) {
       _ = BackgroundTask.Run(
           () => ChatVideoUI.StartCameraWarmup((ChatId)chatId, _disposeCts.Token),
           Log, "JoinVideoCallModal warmup start failed");
   }
   ```

3. **Cancel on dispose without submit**: `DisposeAsync` needs to check
   `_isConfirmed` (set in `OnJoinClick`). If not confirmed, cancel the
   warmup so the recorder doesn't leak.

   ```csharp
   private async ValueTask DisposeAsync(...) {
       // existing cleanup ...
       if (!IsSettingsMode && !ModalModel.IsConfirmed && ModalModel.Chat?.Id is { } chatId)
           await ChatVideoUI.CancelCameraWarmup((ChatId)chatId);
   }
   ```

4. **Settings mode unchanged.** The active recorder already handles the
   in-call preview; no warmup needed.

### Phase 5 — Runtime codec fallback during warmup

If `OnRecordingError` fires while a recorder is in warmup state, walk
to the next codec category and retry. Without this, a codec that passes
`isConfigSupported` but fails at first encode (rare but real) makes
warmup unrecoverable.

`VideoRecorder.cs` adds:

```csharp
private readonly List<string> _excludedCodecCategories = [];

private async Task TryWarmupWithFallback(ChatId chatId, CancellationToken ct)
{
    var attempts = 3; // max codec categories to try
    while (attempts-- > 0) {
        try {
            await Warmup(chatId, ct).ConfigureAwait(false);
            return;
        }
        catch (WarmupCodecFailedException ex) {
            _excludedCodecCategories.Add(ex.FailedCategory);
            // JS-side: pass excluded categories into next Warmup call so it skips them
        }
    }
    throw new InvalidOperationException("All codec categories failed warmup");
}
```

This requires JS-side support for excluding codecs (already partially
exists via `excludeEncoderCodec` in `codec-support.ts`). The
`onRecordingError` callback's `error` string needs to carry the codec
category so the C# fallback knows which one to exclude.

Optional for Phase 1-4; Phase 5 can land separately once 1-4 prove out.

### Phase 6 — Battery/heat safeguards (optional, follow-up)

- Auto-cancel warmup after T_idle without user action (e.g., 5 min).
  `ChatVideoUI` runs a timer alongside the warmup recorder; expired →
  cancel + dispose.
- `framerate: 15` during warmup, `30` on openGate. Requires a TS-side
  frame-rate throttle operator (deferred from P3.2). Land when the
  rest of the bridge is stable.

## Risks

1. **Warmup races with non-modal intent.** If something other than the
   modal fires a `CameraRecordingIntent` (debug UI, programmatic start),
   StateSync's `TryClaimWarmupRecorder` returns null and a fresh
   recorder is built — but the warmup recorder is still alive and now
   competes for the camera/HW slot. Mitigation: idempotent semantics
   in `StartCameraWarmup`; defensive dispose of stranded warmup
   recorders on `StopRecording` of any recorder.
2. **Modal close before warmup resolves.** Warmup is async; if user
   submits before `recorder.Warmup` finishes, `TryClaimWarmupRecorder`
   might race. Solution: `Warmup` task is awaited inside
   `StartCameraWarmup` before returning; `TryClaim` returns null if
   not yet warmed.
3. **Camera permission revoked mid-warmup.** The JS-side `track.onended`
   handler in `warmup` calls `stopRecording`. Same flow as today.
4. **`chatId` mismatch on modal reuse.** Modal can be opened sequentially
   for different chats. Each open must cancel any prior warmup if the
   chatId changed (`StartCameraWarmup` returns false in that case
   today; modal should call cancel first).

## Verification

1. `dotnet build` (ask user).
2. Unit tests:
   - `ChatVideoUI.StartCameraWarmup` idempotency.
   - `TryClaimWarmupRecorder` consumes-once semantics.
3. **Manual happy path**:
   - Open modal, click Start within ~2s. Verify decision log shows the
     encoder was already running before Join (one continuous run, no
     restart). First wire-shipped chunk is a keyframe.
4. **Manual cancel path**:
   - Open modal, click Cancel. Verify JS VideoRecorder is disposed,
     no leaked HW encoder slot (subsequent open works immediately).
5. **Codec fallback (Phase 5)**:
   - Force `isConfigSupported=true` for a codec that the real encoder
     refuses (test-only override). Warmup falls through to next codec
     within a few seconds. User sees no error.
6. **iOS regression check**:
   - On iOS with 1 HW encoder slot, modal-open → submit cycle no
     longer churns the HW slot. Stream comes up immediately on Join.

## Critical files

- `src/dotnet/UI.Blazor.App/Services/VideoRecorder.cs` — new
  `Warmup`/`OpenGate`/`CancelWarmup`/`IsWarmedUp`.
- `src/dotnet/UI.Blazor.App/Services/ChatVideoUI.cs` (or new
  `ChatVideoUI.Warmup.cs`) — warmup ownership, claim semantics.
- `src/dotnet/UI.Blazor.App/Services/ChatVideoUI.StateSync.cs` —
  recorder factoring; `StartCamera` branches on `IsWarmedUp`.
- `src/dotnet/UI.Blazor.App/Components/JoinVideoCallModal/JoinVideoCallModal.razor`
  — drop probe wiring; add warmup kickoff + dispose cancellation.

## Order of work

Phase 1 → 2 → 3 → 4 land the basic flow. Each is independently
reviewable (Phase 1 alone is a no-op API addition; Phase 2 doesn't
affect any caller; Phase 3 adds the branch but `IsWarmedUp` is false
until Phase 4 wires the modal to call warmup). Phase 5 and 6 are
quality improvements; defer until the bridge is stable.

Estimated scope: Phase 1 small, Phase 2 medium, Phase 3 small, Phase 4
small, Phase 5 medium, Phase 6 small. One PR per phase or one bundled
PR — caller's choice.
