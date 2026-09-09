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

That was the first half of the bug. The other half is that the mix — the
microphone on the call link while playback goes over the projection link — is
something the car refuses: it mutes the media channel for as long as it thinks
a call is active. Either both directions ride the call link, or neither does.

## Why the media route alone was not enough

Recording from the phone microphone with a media focus keeps SCO closed, but
the projection link has its own gatekeeper: gearhead forwards the phone's
audio focus to the head unit and only opens the **media channel** when the
head unit grants it. Two things went wrong there on a real car (2026-09-08/09,
Audi head unit):

- **A transient request after the PTT tune is never forwarded.** The tune asks
  for `GAIN_TRANSIENT_MAY_DUCK`; the head unit answers with a guidance-only
  transient. The recording or listening request that follows 10-20 ms later as
  a media `GAIN_TRANSIENT` is dropped by gearhead as "MD already hold sufficient
  focus". The media channel stays closed, the peer's audio is pushed down the
  guidance channel, and the radio keeps playing ducked - straight into the
  phone microphone. A permanent `GAIN` is forwarded even while a transient is
  held, so under projection recording and listening on the media route take
  `AudioFocus.Gain`; the head unit pauses the radio for the burst and resumes
  it when the listening scope releases.
- **A lingering virtual call mutes the media channel.** While SCO is open the
  head unit accepts media focus and pauses the radio, yet plays nothing from
  the projection link - it is in a call. So the app must never leave SCO open
  under the media route: a focus renewal follows every route change, and a
  request off the communication route restores `Mode.Normal` and clears the
  SCO device.

Both directions on the HFP link - the car's microphone and the car's speakers
over SCO - is what every VoIP app does in a car, and the head units tested
cooperate with it: no focus games, echo cancellation in the car's DSP, music
paused for the duration. That is why it is the default.

## The settings

The Android Auto tab offers **one choice with three values**, stored as the
two axes of `UserCarAudioSettings` (`Microphone`, `Output`) so nothing had to
migrate:

| Choice | Stored as | Effect while projecting |
|---|---|---|
| **Car** (default) | `Microphone` ≠ Phone; `Output` ignored | The car's microphone and speakers over Bluetooth HFP, like a phone call. Recording, listening and replay all take a communication focus, playback tracks use `USAGE_VOICE_COMMUNICATION` so they ride the same SCO link the car opened. Music pauses while anyone talks. Some head units show their phone screen instead of navigation for the duration. |
| **Car speakers, phone microphone** | `Microphone` = Phone, `Output` ≠ Phone | Media focus held as a permanent `GAIN`, capture pinned to the built-in mic, playback over the projection link. SCO is never opened. Music pauses while someone is talking. No echo cancellation against the car speakers. |
| **Phone only** | `Microphone` = Phone, `Output` = Phone | Phone microphone and phone speaker; the car is not used for audio. |

`CarAudioRoute.For` turns (projection active, settings) into the route:
`UseCallLink` for Car, otherwise `Input = Builtin` with `Output = External` or
`Builtin`. `CarAudioMode` and its `GetCarAudioMode` / `WithCarAudioMode`
helpers are the only place the two axes and the three choices meet. The zero
default of both axes (`Auto`) reads as Car.

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

A failed read keeps the **last known state** rather than answering "not
projecting": under projection, "not projecting" would mean the communication
route, and a transient provider hiccup must not open a virtual call. A
30-second recheck invalidates the cached state when the provider disagrees
with it, so a missed broadcast heals itself. Every decision - the provider
state, each recomputed route with its inputs, the comm-route choice per focus
request, the route at capture and playback start with the track usage, and
every settings change - is logged at Information, so a drive can be read back
from `logcat` afterwards.

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

On 2026-09-08/09, in an Audi over USB Android Auto, two phones in one chat:

| Setting | Observed |
|---|---|
| Car (SCO both ways) | Peer audible over the car speakers, recording from the car mic, music paused; the head unit shows no phone screen on this car |
| Car speakers, phone mic, transient focus | Media channel never opened (`Not sending focus request to HU as MD already hold sufficient focus`), peer inaudible, radio ducked and captured by the phone mic |
| Car speakers, phone mic, permanent `GAIN` | Head unit granted `GAIN`, `enabling stream: MEDIA`, radio paused; peer still inaudible while a virtual call from the previous mode was left open, which is the leak the renewal fix closes |

## Known gaps

The screen switch itself cannot be reproduced on the Desktop Head Unit. In
a real car the head unit *is* the Bluetooth hands-free device, so an SCO open
looks like an incoming call to it. On the DHU rig the hands-free device is
whatever headset the desk has, and the emulated head unit — attached over
USB — has no telephony profile and never learns that a "call" started. The
projection display stayed on Maps through every run above, including the one
that opens SCO. Confirming the original symptom needs a real head unit.
