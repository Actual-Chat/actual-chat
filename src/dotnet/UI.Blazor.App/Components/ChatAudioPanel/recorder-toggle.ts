import { audioContextSource, recordingAudioContextSource } from "../../../UI.Blazor.App/Services/audio-context-source";

export class RecorderToggle {
    private static isInitialized = false;

    public static async init(): Promise<void> {
        if (this.isInitialized)
            return;

        const buttons = [...document.querySelectorAll<HTMLButtonElement>('div.recorder-wrapper > button')];
        buttons.forEach(btn => {
            btn.addEventListener('click', () => recordingAudioContextSource.initContextInteractively());
            btn.addEventListener('click', () => audioContextSource.initContextInteractively());
        });

        this.isInitialized = true;
    }
}
