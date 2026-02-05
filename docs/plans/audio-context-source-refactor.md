# AudioContextSource Refactoring Summary

## Overview

This document summarizes the refactoring of the audio context management system, introducing a **Trait-based architecture** for cleaner configuration and lifecycle management.

## Changes Made

### 1. New Trait System

**New file: `audio-context-traits.ts`**

Introduced trait interfaces for attaching configuration logic to AudioContext:

```typescript
interface AudioContextTrait {
    readonly name: string;
    attach(context: AppAudioContext): AttachedAudioContextTrait | Promise<AttachedAudioContextTrait>;
}

interface AttachedAudioContextTrait {
    onUsed?(): void | Promise<void>;      // Called when first ref is created
    onUnused?(): void | Promise<void>;    // Called when last ref is disposed
    onClosed?(): void | Promise<void>;    // Called when context is closing
}
```

**DestinationFallbackTrait**: Converted from inline logic to a proper trait that manages the iOS Safari audio element fallback for lock screen media controls.

### 2. Extended AppAudioContext Type

```typescript
export type AppAudioContext = AudioContext & {
    wasInteractive?: boolean;
    traits?: Map<string, AttachedAudioContextTrait>;       // public access to attached traits
    _attachingTraits?: Set<string>;                         // internal, prevents double-attach
};
```

Traits are now stored directly on the AudioContext instance, making them accessible to any code that has the context.

**Destination access**: Use `DestinationFallbackTrait.getDestination(context)` to get the correct destination node. Two static helpers are available:
- `getDestination(context)` - returns fallback or default destination (always a node)
- `getDestinationFallback(context)` - returns fallback node or `undefined`

### 3. Simplified AudioContextSource

- Removed the `AudioContextSource` interface (was unnecessary abstraction)
- Renamed `WebAudioContextSource` to `AudioContextSource`
- Reorganized class structure: public methods first, private methods in a single section
- Renamed debounced fields to use underscore prefix (`_suspendContextDebounced`, `_closeContextDebounced`)
- Renamed lifecycle methods: `onFirstRefAcquired` → `onFirstRefCreated`, `onLastRefReleased` → `onLastRefDisposed`
- Renamed `hasRefsInUse` → `isUsed`
- Removed unused `onDeviceAwakeHandler` field (singleton doesn't need to store it)

### 4. Updated Consumers

**audio-player.ts**: Created `FeederNodeTrait` for managing the audio worklet lifecycle.

**opus-media-recorder.ts**: Created `RecordingPipelineTrait` for managing VAD and encoder worklets.

**sound-player.ts**: Simplified to use `audioContextSource.run()` directly without custom traits.

### 5. Deleted Files

- `audio-context-ref.ts` (merged into `audio-context-source.ts`)
- `audio-context-trait.ts` (merged into `audio-context-traits.ts`)
- `audio-context-destination-fallback.ts` (merged into `audio-context-traits.ts`)

## Why These Changes Don't Break Anything

### AudioContext Creation and User Gesture Requirements

**Key insight**: AudioContext can be **created** at any time (it starts in `suspended` state), but `context.resume()` must be called in the **synchronous call stack** of a user gesture (click/touch).

#### How the System Works

1. **Early Creation (not in user gesture)**:
   ```
   App startup
     → constructor()
       → delayAsync(300).then(() => this.maintain())
         → maintain()
           → create()
             → new AudioContext()  // Created in SUSPENDED state
             → interactiveResume(context)  // Waits for user gesture
   ```

2. **Resume in User Gesture (synchronous path)**:
   ```
   User clicks button (sync)
     → Interactive.onInteractionEvent() (sync - event listener)
       → interactionEvents.triggerSilently(event) (sync)
         → handler callback (sync)
           → resume(context, true) (async function called, but...)
             → context.resume() (line 696) // SYNC - before any await!
   ```

   The critical `context.resume()` call happens **before** any `await` in the `resume()` method, keeping it in the synchronous user gesture stack.

3. **initContextInteractively() Path**:
   ```
   User clicks recorder/playback button (sync)
     → btn.addEventListener('click', () => ...) (sync)
       → initContextInteractively() (async, but called sync)
         → resume(context, true)
           → context.resume() // SYNC - in user gesture stack
   ```

### Why Trait Attachment is Safe

Traits are attached in `create()` after the context is created:

```typescript
private async create(shouldResume = false): Promise<AudioContext> {
    const context = new AudioContext({...});

    if (shouldResume)
        await this.resume(context, true);  // Resume first if in gesture

    // ... load worklets ...

    await this.attachAllTraits(context);  // Safe - happens after resume

    return context;
}
```

The trait attachment happens **after** the context is resumed (if needed), so it doesn't interfere with the user gesture requirement.

### Why Moving Traits to AppAudioContext is Safe

Previously, `_attachedTraits` was on `WebAudioContextSource`. Now it's on `AppAudioContext` itself (`context.traits_`).

This is safe because:
1. Each AudioContext instance has its own traits map
2. When context is closed/recreated, the old traits are naturally garbage collected with the old context
3. The `attachingTraits_` set prevents double-attachment during async operations

### Why Removing the Interface is Safe

The `AudioContextSource` interface was only implemented by `WebAudioContextSource`. Since we export concrete instances (`audioContextSource`, `recordingAudioContextSource`), consumers never needed the interface for polymorphism. Removing it simplifies the code without changing behavior.

### Why Singleton Pattern Works

`AudioContextSource` is documented as a singleton (one per purpose: 'playback' or 'recording'). This means:
- Device wake event handler doesn't need cleanup (lives as long as the app)
- Debounced functions don't need cleanup
- The maintain loop runs for the app's lifetime

## Entry Points for User Interaction

### Playback Context

1. **playback-toggle.ts:12**: `btn.addEventListener('click', () => audioContextSource.initContextInteractively())`
2. **recorder-toggle.ts:14**: `btn.addEventListener('click', () => audioContextSource.initContextInteractively())`

### Recording Context

1. **recorder-toggle.ts:13**: `btn.addEventListener('click', () => recordingAudioContextSource.initContextInteractively())`
2. **playback-toggle.ts:12**: `btn.addEventListener('click', () => recordingAudioContextSource.initContextInteractively())`

Both contexts use the same `initContextInteractively()` → `resume()` → `context.resume()` path, ensuring the resume call is in the user gesture stack.

## Trait Lifecycle

```
Context Created
  → attachAllTraits()
    → for each trait: attachTrait()
      → trait.attach(context) → AttachedTrait
      → context.traits_.set(name, attached)
      → if refs exist: attached.onUsed()

First Ref Created
  → onFirstRefCreated()
    → for each attached: attached.onUsed()

Last Ref Disposed
  → onLastRefDisposed()
    → for each attached: attached.onUnused()

Context Closing
  → detachAllTraits()
    → for each attached: attached.onClosed()
    → context.traits_.clear()
```

## Testing Checklist

1. **Playback**: Play an audio message - verify audio plays correctly
2. **Recording**: Record a voice message - verify recording works
3. **Sound effects**: Trigger UI sounds - verify they play
4. **Context recovery**: Background/foreground the app - verify audio recovers
5. **Device wake**: Sleep/wake the device - verify audio recovers
6. **iOS lock screen**: Verify media controls appear on iOS lock screen
