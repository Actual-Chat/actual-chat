import { getLogs } from 'logging';

const { logScope, debugLog, warnLog } = getLogs('OutgoingCallRingback');

// European ringback tone: a 425 Hz sine, 1s on / 4s off. Synthesized into a short
// WAV (a standard telephony signal — no asset or licensing needed) and looped via an HTMLAudioElement,
// the same mechanism as the incoming ringtone: media playback keeps going across tab backgrounding,
// unlike a Web Audio graph whose managed AudioContext is torn down and re-created on foreground.
const SampleRate = 8000;
const Frequency = 425;
const ToneSec = 1;
const PauseSec = 4;
const Volume = 0.2;
const RampSec = 0.01;

export class OutgoingCallRingback {
    private static audio: HTMLAudioElement | null = null;
    private static url: string | null = null;

    /** Called by blazor */
    public static start(): void {
        if (this.audio)
            return;

        try {
            const url = URL.createObjectURL(buildRingbackWav());
            const audio = new Audio(url);
            audio.loop = true;
            this.audio = audio;
            this.url = url;
            debugLog?.log(`${logScope}.start`);
            audio.play().catch((e: unknown) => warnLog?.log(`${logScope}.start: play failed`, e));
        } catch (e) {
            warnLog?.log(`${logScope}.start: failed`, e);
            this.stop();
        }
    }

    /** Called by blazor */
    public static stop(): void {
        const { audio, url } = this;
        this.audio = null;
        this.url = null;
        if (!audio && !url)
            return;

        debugLog?.log(`${logScope}.stop`);
        try {
            audio?.pause();
        } catch (e) {
            warnLog?.log(`${logScope}.stop: failed`, e);
        }
        if (url)
            URL.revokeObjectURL(url);
    }
}

function buildRingbackWav(): Blob {
    const total = Math.floor(SampleRate * (ToneSec + PauseSec));
    const toneSamples = Math.floor(SampleRate * ToneSec);
    const rampSamples = Math.max(1, Math.floor(SampleRate * RampSec));
    const samples = new Int16Array(total); // the pause segment stays zero-filled (silence)
    for (let i = 0; i < toneSamples; i++) {
        let amp = Volume;
        if (i < rampSamples)
            amp = Volume * (i / rampSamples);
        else if (i > toneSamples - rampSamples)
            amp = Volume * ((toneSamples - i) / rampSamples);
        const value = Math.sin(2 * Math.PI * Frequency * (i / SampleRate)) * amp;
        samples[i] = Math.round(Math.max(-1, Math.min(1, value)) * 0x7fff);
    }
    return encodeWav(samples, SampleRate);
}

function encodeWav(samples: Int16Array, sampleRate: number): Blob {
    const dataBytes = samples.length * 2;
    const buffer = new ArrayBuffer(44 + dataBytes);
    const view = new DataView(buffer);
    const writeStr = (offset: number, text: string) => {
        for (let i = 0; i < text.length; i++)
            view.setUint8(offset + i, text.charCodeAt(i));
    };
    writeStr(0, 'RIFF');
    view.setUint32(4, 36 + dataBytes, true);
    writeStr(8, 'WAVE');
    writeStr(12, 'fmt ');
    view.setUint32(16, 16, true);
    view.setUint16(20, 1, true); // PCM
    view.setUint16(22, 1, true); // mono
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, sampleRate * 2, true); // byte rate
    view.setUint16(32, 2, true); // block align
    view.setUint16(34, 16, true); // bits per sample
    writeStr(36, 'data');
    view.setUint32(40, dataBytes, true);
    for (let i = 0; i < samples.length; i++)
        view.setInt16(44 + i * 2, samples[i], true);
    return new Blob([buffer], { type: 'audio/wav' });
}
