import { audioContextSource, recordingAudioContextSource } from '../../Services/audio-context-source';

export class PlaybackToggle {
    private static isInitialized = false;

    public static async init(): Promise<void> {
        if (this.isInitialized)
            return;

        const buttons = [...document.querySelectorAll<HTMLButtonElement>('div.playback-wrapper > button')];
        buttons.forEach(btn => {
            btn.addEventListener('click', () => recordingAudioContextSource.initContextInteractively());
            btn.addEventListener('click', () => audioContextSource.initContextInteractively());
        });

        this.isInitialized = true;
    }
}
