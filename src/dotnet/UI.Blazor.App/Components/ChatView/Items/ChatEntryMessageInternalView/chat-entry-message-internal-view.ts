// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition */
import { fastRaf10 } from 'fast-raf';

// A shrink has to hold for this long before it's applied - re-recognition routinely makes the
// transcript briefly shorter, and following that down and back up reads as jitter.
const ShrinkDelayMs = 1500;

const streamingViews = new Set<ChatEntryMessageInternalView>();
// selectNodeContents resets it, so one Range serves every entry - this runs 10x/s.
const measureRange = document.createRange();
let isHeightLoopRunning = false;

// One pass for every streaming entry, rather than an observer and a rAF per entry, split across
// the scheduler's read and write phases. That keeps the measurements clear of every write due in
// that frame rather than just of this loop's own, so a pass costs one forced layout no matter how
// many entries are streaming; and landing on the 100ms grid puts the writes in frames the synced
// animations were changing anyway, which is what the rendering cost is actually per.
function runHeightPass(): void {
    if (streamingViews.size === 0) {
        isHeightLoopRunning = false;
        return;
    }

    const pending = new Array<[ChatEntryMessageInternalView, number]>();
    fastRaf10({
        read: time => {
            for (const view of streamingViews) {
                const height = view.getPendingHeight(time);
                if (height !== null)
                    pending.push([view, height]);
            }
        },
        write: () => {
            for (const [view, height] of pending)
                view.setHeight(height);
            runHeightPass();
        },
    });
}

export class ChatEntryMessageInternalView {
    private readonly messageMarkup: HTMLElement;
    private playableText: HTMLElement | null;
    private height = -1;
    private shrinkPendingSince = 0;
    private isDisposed = false;
    private classObserver: MutationObserver;
    private createPlayableTextObserver: MutationObserver;

    static create(messageMarkup: HTMLElement): ChatEntryMessageInternalView {
        return new ChatEntryMessageInternalView(messageMarkup);
    }

    constructor(messageMarkup: HTMLElement) {
        this.messageMarkup = messageMarkup;
        if (!this.messageMarkup)
            return;

        const isStreaming = this.messageMarkup.classList.contains('streaming');

        this.classObserver = new MutationObserver(this.stateHandler);
        this.classObserver.observe(this.messageMarkup, {
            attributes: true,
            attributeFilter: ['class'],
        });

        this.playableText = this.messageMarkup.querySelector('.playable-text-markup');
        if (isStreaming)
            this.startStreaming();
        if (!this.playableText && isStreaming) {
            this.createPlayableTextObserver = new MutationObserver(this.smoothShowPlayableText);
            this.createPlayableTextObserver.observe(this.messageMarkup, {
                childList: true,
                subtree: true,
            });
        }
    }

    public dispose() {
        if (this.isDisposed)
            return;

        this.isDisposed = true;
        this.stopStreaming();
        this.classObserver?.disconnect();
        this.createPlayableTextObserver?.disconnect();
    }

    // The height loop reaches these two, in that order, across all streaming entries - the read
    // phase must stay free of writes for the batching to be worth anything.
    public getPendingHeight(time: number): number | null {
        const height = this.measureHeight();
        if (height > this.height) {
            this.shrinkPendingSince = 0;
            return height;
        }
        if (height === this.height) {
            this.shrinkPendingSince = 0;
            return null;
        }

        if (this.shrinkPendingSince === 0) {
            this.shrinkPendingSince = time;
            return null;
        }
        return time - this.shrinkPendingSince >= ShrinkDelayMs ? height : null;
    }

    public setHeight(height: number) {
        this.height = height;
        this.shrinkPendingSince = 0;
        this.messageMarkup.style.height = `${height}px`;
    }

    // Private methods

    // An explicit height for the whole streaming lifetime: `height: auto` doesn't interpolate,
    // so any transition starting from it fires instantly instead of animating.
    private startStreaming() {
        if (streamingViews.has(this))
            return;

        streamingViews.add(this);
        if (isHeightLoopRunning)
            return;

        isHeightLoopRunning = true;
        runHeightPass();
    }

    private stopStreaming() {
        if (!streamingViews.delete(this))
            return;

        this.height = -1;
        this.shrinkPendingSince = 0;
        this.messageMarkup.style.height = '';
    }

    private measureHeight(): number {
        measureRange.selectNodeContents(this.messageMarkup);
        return Math.ceil(measureRange.getBoundingClientRect().height);
    }

    // Reads the current state rather than diffing added classes: the streaming class is also
    // dropped for no class at all, which a diff of additions never sees - and both calls below
    // are idempotent, so there's nothing a diff would buy.
    private stateHandler: MutationCallback = () => {
        if (this.messageMarkup.classList.contains('streaming'))
            this.startStreaming();
        else
            this.stopStreaming();
    };

    private smoothShowPlayableText: MutationCallback = (mutations) => {
        mutations.forEach(mutation => {
            if (mutation.type !== 'childList')
                return;

            mutation.addedNodes.forEach((node) => {
                if (node instanceof HTMLElement && node.classList.contains('playable-text-markup'))
                    node.classList.add('smooth-show');
            });
        });
    }
}
