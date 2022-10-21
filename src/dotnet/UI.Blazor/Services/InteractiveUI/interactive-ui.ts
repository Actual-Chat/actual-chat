import { audioContextLazy } from 'audio-context-lazy';
import { delayAsync } from 'promises';
import { EventHandler, EventHandlerSet } from 'event-handling';
import { NextInteraction } from 'next-interaction';

const LogScope: string = 'InteractiveUI';
const debug = true;

export class InteractiveUI {
    private static backendRef: DotNet.DotNetObject = null;
    private static backendIsInteractive: boolean = false;
    private static isSyncing: boolean = false;
    private static onAudioContextChanged: EventHandler<AudioContext | null> = null;

    public static isInteractive: boolean = false;
    public static isInteractiveChanged: EventHandlerSet<boolean> = new EventHandlerSet<boolean>();

    public static init(backendRef: DotNet.DotNetObject) {
        this.backendRef = backendRef;
        this.onAudioContextChanged = audioContextLazy.audioContextChanged.add(() => this.trySync());
        NextInteraction.start();
    }

    public static trySync() : void {
        const isInteractive = audioContextLazy.audioContext != null;
        if (this.isInteractive == isInteractive)
            return;

        if (debug)
            console.debug(`${LogScope}: isInteractive():`, isInteractive);
        this.isInteractive = isInteractive;
        this.isInteractiveChanged.triggerSilently(isInteractive);
        if (this.isSyncing)
            return;

        if (isInteractive != this.backendIsInteractive)
            void this.sync();
    }

    private static async sync() : Promise<void> {
        if (this.isSyncing)
            return; // Sync is already in progress, it will do the job anyway

        this.isSyncing = true;
        try {
            while (true) {
                const isInteractive = this.isInteractive; // We need a stable copy here
                if (isInteractive == this.backendIsInteractive)
                    break;
                try {
                    if (debug)
                        console.debug(`${LogScope}: sync(): calling IsInteractiveChanged(${isInteractive}) on backend`);
                    await this.backendRef.invokeMethodAsync("IsInteractiveChanged", isInteractive);
                    this.backendIsInteractive = isInteractive;
                }
                catch (error) {
                    console.error(`${LogScope}: sync() failed to reach the backend, error:`, error);
                    await delayAsync(1000);
                }
            }
        }
        finally {
            this.isSyncing = false;
        }
    }
}
