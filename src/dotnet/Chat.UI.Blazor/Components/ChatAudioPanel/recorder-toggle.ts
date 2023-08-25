import { audioContextSource } from "../../../Audio.UI.Blazor/Services/audio-context-source";

export class RecorderToggle {
    private static isInitialized = false;

    public static async init(): Promise<void> {
        if (this.isInitialized)
            return;

        const buttons = [...document.querySelectorAll<HTMLButtonElement>('div.recorder-wrapper > button')];
        buttons.forEach(btn =>
            btn.addEventListener('click', () => {
                void audioContextSource.initContextInteractively();
            }));
        this.isInitialized = true;
    }
}
