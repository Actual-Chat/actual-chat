import { fromEvent, Subject, takeUntil } from 'rxjs';
import { Disposable } from 'disposable';

export interface HoverSelectOptions {
    resolveKey: (target: Element | null) => string | null;
    onSelect: (key: string) => void;
}

// Hover selection for keyboard-navigable lists: an item is selected only when the pointer
// actually moves over it. A re-render or a scroll under a resting cursor also fires pointer
// events, but with unchanged coordinates - such events can't steal the keyboard selection.
export class HoverSelect implements Disposable {
    private readonly disposed$ = new Subject<void>();
    private key: string | null = null;
    private lastX = Number.NaN;
    private lastY = Number.NaN;

    constructor(host: HTMLElement, private readonly options: HoverSelectOptions) {
        fromEvent<PointerEvent>(host, 'pointermove')
            .pipe(takeUntil(this.disposed$))
            .subscribe(event => this.onPointerMove(event));
        // Forgetting the item on leave makes a move back onto it count as a fresh hover.
        fromEvent<PointerEvent>(host, 'pointerleave')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.key = null);
    }

    public dispose(): void {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    // Private methods

    private onPointerMove(event: PointerEvent): void {
        if (event.pointerType === 'touch')
            return;
        if (event.clientX === this.lastX && event.clientY === this.lastY)
            return;

        this.lastX = event.clientX;
        this.lastY = event.clientY;
        const key = this.options.resolveKey(event.target as Element | null);
        if (key === this.key)
            return;

        this.key = key;
        if (key !== null)
            this.options.onSelect(key);
    }
}
