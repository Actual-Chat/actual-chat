# 11 — Android Auto

Recording a voice message while the phone projects into a car head unit used to
make the car switch its screen from navigation to the phone/contacts view, and
the first recording was swallowed. This doc explains why that happens, what the
app does instead, and what the user can change.

Like PTT (doc 10), nothing here changes a frame's path through the pipeline:
capture, VAD, Opus framing, publish, fan-out and playback are untouched. This
layer only decides **which physical microphone and which speaker** sit at the
ends of that path while projection is active.

## Why a car reacts to a recording at all

Two independent links run between phone and car:

| Link | Carries | Direction |
|---|---|---|
| **USB projection** (Android Auto) | the head unit's screen, touch, and media audio | audio out only |
| **Bluetooth HFP** | call audio, over an SCO channel | bidirectional, mono |

The projection link cannot carry a microphone: it does not capture
communication-usage audio. So when the app asked for a *communication* audio
focus in order to record — the natural request for a voice message — Android
had exactly one bidirectional path available and took it: it opened **SCO**.

Opening SCO outside a real telephony call is an **HFP virtual call**. To the
head unit it is indistinguishable from a call starting, so the car does what a
car does for a call: it takes over its own screen. The swallowed first
recording is the same event seen from the app's side — the microphone produces
nothing usable until the SCO channel has finished negotiating, which takes
noticeably longer than a person's patience before speaking.

That is the whole bug. The fix is to stop asking for a route the app doesn't
need: a *media* focus records perfectly well from the phone's own microphone
and never makes Android open SCO.

## The settings

The behaviour is not hardcoded, because the right answer is a property of the
vehicle rather than of the app. A head unit's built-in microphone array can be
genuinely better than a phone lying in a cupholder — and a driver who prefers
it should be able to say so, and accept the screen switch that comes with it.

There are **two independent axes**, three values each:

### Microphone — where recording is captured from

| Value | Effect while projecting |
|---|---|
| **Auto** (default) | The phone's own microphone. The app takes a media audio focus and pins capture to the built-in mic, so SCO is never opened and the car keeps its screen. |
| **Phone** | Identical to Auto today. It exists as an explicit choice so that a user who wants the phone microphone keeps it even if the meaning of Auto is ever revised. |
| **Car** | The car's microphone, over Bluetooth HFP. The app asks for a communication focus, SCO opens, and the head unit may well switch to its phone screen — that is the cost of this option, not a defect. |

### Sound — where playback goes

| Value | Effect while projecting |
|---|---|
| **Auto** (default) | The car's speakers, through the projection link. |
| **Phone** | The phone's own speaker. Playback is pinned there before any sound starts, and that pin deliberately never falls back to a Bluetooth device — which also means this branch skips the ordinary route selection entirely, since a Bluetooth pick there would raise the same virtual call the microphone side avoids. |
| **Car** | Identical to Auto today, and exists for the same reason Phone does on the microphone axis. |

### What the defaults mean

Both axes default to **Auto**, and Auto is deliberately *not* "let Android
decide" — it is a concrete pair of choices (phone microphone, car speakers)
that happens to be the combination which avoids the bug. A user who never opens
this tab gets the fixed behaviour.

### When the settings apply

**Only while the phone is actually projecting into a car.** With no projection
the app imposes nothing at all and the platform keeps its own device
priority — exactly the behaviour that existed before this feature, including
the ordinary Bluetooth-headset handling everywhere else in the app. This
matters: the settings are not a general "always use the phone microphone"
switch, and they cannot be used to work around headset problems outside a car.

The values are stored **per user**, not per device, so they follow the account
to another phone. The trade-off is deliberate but worth knowing: someone who
drives two cars with different head units gets one setting for both.

## Where the tab is

Settings has an **Android Auto** tab, placed directly after Application.

The tab is shown **only where car projection can be detected at all** — in
practice, on the Android app. On iOS, on the desktop apps and on the web the
tab does not exist, because nothing there can answer the question these
settings depend on. There is no "unsupported" placeholder and no greyed-out
section: the entry is simply absent.

Note that this is availability, not activity — the tab is reachable whenever
the app runs on a supported platform, whether or not a car is connected right
now. That is intentional: the settings are most useful when configured *before*
driving, and a tab that appeared only while plugged into a car would have to be
found and changed at the exact moment the driver should not be looking at the
phone. What depends on an actual connection is the *effect* of the settings,
described above, not their visibility.

## How the app detects the car

The phone knows when Android Auto is projecting, and the app asks the system
for that state rather than inferring it from audio devices. It re-checks
whenever the system announces a connection change, and again whenever the app
returns to the foreground — a car can be plugged in or unplugged while the app
is stopped.

Every failure in that detection is treated as **"not projecting"**. If the
state cannot be read, the app behaves exactly as it did before this feature
existed. Nothing about recording depends on the detector working; it can only
add the car-specific handling, never take away the ordinary one.

## What was measured

On 2026-08-30, on an Android phone with a Bluetooth headset connected and a
projection session active:

| Setting | Observed |
|---|---|
| Microphone = Auto | Media audio focus, audio mode unchanged, capture from the built-in microphone, no SCO traffic at all |
| Microphone = Car | Communication audio focus, SCO opened, capture from the headset microphone, released cleanly afterwards |
| Sound = Phone | Playback pinned to the phone's speaker, audible from the phone |
| Sound = Car | Ordinary media playback, no communication-device traffic |

The headset stayed connected throughout, so the SCO path was available in every
run and was taken only when it was asked for.

## Known gaps

The screen switch itself cannot be reproduced on the Desktop Head Unit. In
a real car the head unit *is* the Bluetooth hands-free device, so an SCO open
looks like an incoming call to it. On the DHU rig the hands-free device is
whatever headset the desk has, and the emulated head unit — attached over
USB — has no telephony profile and never learns that a "call" started. The
projection display stayed on Maps through every run above, including the one
that opens SCO. Confirming the original symptom needs a real head unit.
