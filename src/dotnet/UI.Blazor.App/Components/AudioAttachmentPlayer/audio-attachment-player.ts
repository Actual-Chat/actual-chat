import { getLogs } from 'logging';

const { warnLog } = getLogs('AudioAttachmentPlayer');

document.addEventListener('input', (e: Event) => {
    const t = e.target;
    if (t instanceof HTMLInputElement && t.classList.contains('c-progress-input'))
        t.parentElement?.style.setProperty('--progress', t.value);
}, true);

let scrubbing: HTMLElement | null = null;
document.addEventListener('pointerdown', (e: PointerEvent) => {
    const t = e.target;
    if (t instanceof HTMLInputElement && t.classList.contains('c-progress-input')) {
        const parent = t.parentElement;
        if (!parent)
            return;

        scrubbing = parent;
        parent.classList.add('is-scrubbing');
    }
}, true);
const endScrub = () => {
    if (!scrubbing)
        return;

    scrubbing.classList.remove('is-scrubbing');
    scrubbing = null;
};
document.addEventListener('pointerup', endScrub, true);
document.addEventListener('pointercancel', endScrub, true);

export class AudioAttachmentPlayer {
    private readonly audio: HTMLAudioElement;
    private disposed = false;

    static create(blazorRef: DotNet.DotNetObject): AudioAttachmentPlayer {
        return new AudioAttachmentPlayer(blazorRef);
    }

    constructor(private readonly blazorRef: DotNet.DotNetObject) {
        this.audio = new Audio();
        this.audio.preload = 'auto';
        this.audio.addEventListener('play', this.onPlay);
        this.audio.addEventListener('pause', this.onPause);
        this.audio.addEventListener('ended', this.onEnded);
        this.audio.addEventListener('timeupdate', this.onTimeUpdate);
        this.audio.addEventListener('durationchange', this.onDurationChange);
        this.audio.addEventListener('loadedmetadata', this.onDurationChange);
        this.audio.addEventListener('error', this.onError);
    }

    public dispose() {
        if (this.disposed)
            return;

        this.disposed = true;
        this.audio.removeEventListener('play', this.onPlay);
        this.audio.removeEventListener('pause', this.onPause);
        this.audio.removeEventListener('ended', this.onEnded);
        this.audio.removeEventListener('timeupdate', this.onTimeUpdate);
        this.audio.removeEventListener('durationchange', this.onDurationChange);
        this.audio.removeEventListener('loadedmetadata', this.onDurationChange);
        this.audio.removeEventListener('error', this.onError);
        this.audio.pause();
        this.audio.src = '';
    }

    public async play(url: string): Promise<void> {
        if (this.audio.src !== url)
            this.audio.src = url;
        this.audio.currentTime = 0;
        try {
            await this.audio.play();
        }
        catch (e) {
            warnLog?.log('play failed:', e);
            void this.blazorRef.invokeMethodAsync('OnError', String(e));
        }
    }

    public pause(): void {
        this.audio.pause();
    }

    public async resume(): Promise<void> {
        try {
            if (this.audio.ended)
                this.audio.currentTime = 0;
            await this.audio.play();
        }
        catch (e) {
            warnLog?.log('resume failed:', e);
            void this.blazorRef.invokeMethodAsync('OnError', String(e));
        }
    }

    public stop(): void {
        this.audio.pause();
        this.audio.currentTime = 0;
        this.audio.src = '';
    }

    public seek(positionSec: number): void {
        if (!isFinite(positionSec))
            return;

        this.audio.currentTime = positionSec;
    }

    // Private methods

    private onPlay = (): void => {
        void this.blazorRef.invokeMethodAsync('OnPlay');
    };

    private onPause = (): void => {
        if (this.audio.ended)
            return;

        void this.blazorRef.invokeMethodAsync('OnPause');
    };

    private onEnded = (): void => {
        void this.blazorRef.invokeMethodAsync('OnEnded');
    };

    private onTimeUpdate = (): void => {
        void this.blazorRef.invokeMethodAsync('OnTimeUpdate', this.audio.currentTime);
    };

    private onDurationChange = (): void => {
        const d = this.audio.duration;
        if (!isFinite(d) || isNaN(d))
            return;

        void this.blazorRef.invokeMethodAsync('OnDurationChange', d);
    };

    private onError = (): void => {
        const err = this.audio.error;
        const msg = err ? `code=${err.code} ${err.message}` : 'unknown';
        warnLog?.log('audio error:', msg);
        void this.blazorRef.invokeMethodAsync('OnError', msg);
    };
}
