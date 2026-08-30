import { initAudioContextsOnClick } from '../../Services/audio-context-source';

export class RecorderToggle {
    private static isInitialized = false;

    public static init(): void {
        if (this.isInitialized)
            return;

        this.isInitialized = true;
        initAudioContextsOnClick('div.recorder-wrapper > button');
    }
}
