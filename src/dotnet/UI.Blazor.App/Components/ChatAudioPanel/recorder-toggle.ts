import { audioContextSource, recordingAudioContextSource } from '../../Services/audio-context-source';

export class RecorderToggle {
    private static isInitialized = false;
    private static cleanupCallbacks: (() => void)[] = [];
    private static observer: MutationObserver | null = null;

    public static async init(): Promise<void> {
        if (this.isInitialized)
            return;

        const buttons = [...document.querySelectorAll<HTMLButtonElement>('div.recorder-wrapper > button')];
        buttons.forEach(btn => {
            const handler1 = () => recordingAudioContextSource.initContextInteractively();
            const handler2 = () => audioContextSource.initContextInteractively();

            btn.addEventListener('click', handler1);
            btn.addEventListener('click', handler2);

            this.cleanupCallbacks.push(() => {
                btn.removeEventListener('click', handler1);
                btn.removeEventListener('click', handler2);
            });
        });

        const observeTarget = document.querySelector('div.recorder-button');
        const audioPanel = observeTarget?.closest('div.chat-audio-panel');
        if (observeTarget && audioPanel) {
            this.observer = new MutationObserver(mutations => {
                mutations.forEach(mutation => {
                    if (mutation.type === 'attributes' &&
                        mutation.attributeName === 'class' &&
                        mutation.target instanceof HTMLElement) {
                        const el = mutation.target;
                        const newClassList = el.classList;
                        const oldClassList = (mutation.oldValue ?? '').split(/\s+/);

                        if (newClassList.contains('record-on') && !oldClassList.includes('record-on')) {
                            if (!audioPanel.classList.contains('has-record-on'))
                                audioPanel.classList.add('has-record-on');
                        }
                        if (!newClassList.contains('record-on') && oldClassList.includes('record-on')) {
                            audioPanel.classList.remove('has-record-on');
                        }
                        if (newClassList.contains('record-on-btn') && !oldClassList.includes('record-on-btn')) {
                            if (!audioPanel.classList.contains('has-record-on-btn'))
                                audioPanel.classList.add('has-record-on-btn');
                        }
                        if (!newClassList.contains('record-on-btn') && oldClassList.includes('record-on-btn')) {
                            audioPanel.classList.remove('has-record-on-btn');
                        }
                    }
                });
            });

            this.observer.observe(observeTarget, {
                attributes: true,
                attributeFilter: ['class'],
                attributeOldValue: true,
                subtree: true
            });

            this.cleanupCallbacks.push(() => this.observer?.disconnect());
        }

        this.isInitialized = true;
    }

    public static dispose(): void {
        this.cleanupCallbacks.forEach(cleanup => cleanup());
        this.cleanupCallbacks = [];
        this.isInitialized = false;
    }
}
