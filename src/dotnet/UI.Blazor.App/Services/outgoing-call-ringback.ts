import { getLogs } from 'logging';
import { Disposable } from 'disposable';
import { SoundPlayer } from '../../UI.Blazor/Services/TuneUI/sound-player';

const { logScope, debugLog } = getLogs('OutgoingCallRingback');

// European ringback tone: a 425 Hz sine, 1s on / 4s off. Synthesized (a standard telephony
// signal - no asset or licensing needed) and looped the same way as the incoming ringtone.
const SampleRate = 8000;
const Frequency = 425;
const ToneSec = 1;
const PauseSec = 4;
const Volume = 0.2;
const RampSec = 0.01;

export class OutgoingCallRingback {
    private static ring: Disposable | null = null;

    /** Called by blazor */
    public static start(): void {
        if (this.ring)
            return;

        debugLog?.log(`${logScope}.start`);
        this.ring = SoundPlayer.instance.loop(buildRingback());
    }

    /** Called by blazor */
    public static stop(): void {
        const ring = this.ring;
        if (!ring)
            return;

        this.ring = null;
        debugLog?.log(`${logScope}.stop`);
        ring.dispose();
    }
}

function buildRingback(): AudioBuffer {
    const total = Math.floor(SampleRate * (ToneSec + PauseSec));
    const toneSamples = Math.floor(SampleRate * ToneSec);
    const rampSamples = Math.max(1, Math.floor(SampleRate * RampSec));
    const buffer = new AudioBuffer({ length: total, sampleRate: SampleRate });
    const samples = buffer.getChannelData(0); // the pause segment stays zero-filled (silence)
    for (let i = 0; i < toneSamples; i++) {
        let amp = Volume;
        if (i < rampSamples)
            amp = Volume * (i / rampSamples);
        else if (i > toneSamples - rampSamples)
            amp = Volume * ((toneSamples - i) / rampSamples);
        samples[i] = Math.sin(2 * Math.PI * Frequency * (i / SampleRate)) * amp;
    }
    return buffer;
}
