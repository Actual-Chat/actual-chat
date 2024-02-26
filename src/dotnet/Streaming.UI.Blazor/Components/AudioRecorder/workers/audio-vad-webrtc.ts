import { WebRtcVad } from '@actual-chat/webrtc-vad';
import { VoiceActivityDetectorBase } from './audio-vad';

const SAMPLES_PER_WINDOW_48K = 1440; // 30ms at 48000 Hz

enum VadActivity {
    Silence = 0,
    Voice = 1,
    Error = -1,
}

export class WebRtcVoiceActivityDetector extends VoiceActivityDetectorBase {
    constructor(private vad: WebRtcVad) {
        super(48000, false);
    }

    public override init(): Promise<void> {
        // @ts-ignore
        return Promise.resolve(undefined);
    }

    protected override appendChunkInternal(monoPcm: Float32Array): Promise<number | null> {
        if (monoPcm.length !== SAMPLES_PER_WINDOW_48K)
            throw new Error(`appendChunk() accepts ${SAMPLES_PER_WINDOW_48K} sample audio windows only.`);

        const activity = this.vad.detect(monoPcm.buffer);
        if (activity == VadActivity.Error)
            throw new Error(`Error calling WebRtc VAD`);

        // Our base class logic has been developed for float speech probability about 0.75 and higher,
        // so let's adjust 1|0 to tested range to reuse existing heuristics
        return Promise.resolve(Number(0.8 * activity));
    }

    protected override resetInternal() {
        this.vad.reset();
    }
}
