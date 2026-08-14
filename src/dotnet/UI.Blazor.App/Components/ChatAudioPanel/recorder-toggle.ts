// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/require-await, @typescript-eslint/no-floating-promises */
import { audioContextSource, recordingAudioContextSource } from '../../Services/audio-context-source';

export class RecorderToggle {
    private static isInitialized = false;

    public static async init(): Promise<void> {
        if (this.isInitialized)
            return;

        const buttons = [...document.querySelectorAll<HTMLButtonElement>('div.recorder-wrapper > button')];
        buttons.forEach(btn => {
            btn.addEventListener('click', () => {
                recordingAudioContextSource.initContextInteractively();
                audioContextSource.initContextInteractively();
            });
        });

        this.isInitialized = true;
    }
}
