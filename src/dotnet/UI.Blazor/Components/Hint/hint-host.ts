import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil } from 'rxjs';
import { autoUpdate, Placement } from '@floating-ui/dom';
import { MutationProcessor } from 'mutation-processor';
import { getLogs } from 'logging';
import { BubbleHost } from '../Bubble/bubble-host';

const { errorLog } = getLogs('HintHost');

// `data-render-script-hint` points a bubble-styled hint at its element for as long as the value is
// non-empty: the markup that sets it owns the hint's lifetime. Unlike an onboarding bubble it is
// neither queued nor remembered, and unlike a tooltip it is meant to be seen next to one.
const HintScript = 'hint';

/** A contextual hint next to an element, shown and cleared by the `data-render-script-hint` value. */
export class HintHost implements Disposable {
    private static _current: HintHost | null = null;
    private readonly _disposed$: Subject<void> = new Subject<void>();
    private readonly _hintRef: HTMLElement;
    private readonly _hintTextRef: HTMLElement;
    private readonly _arrowRef: HTMLElement;
    private _shownElement: HTMLElement | null = null;
    private _clearPositionUpdate: (() => void) | null = null;

    public static create(): HintHost {
        return new HintHost();
    }

    constructor() {
        try {
            this._hintRef = document.getElementsByClassName('ac-hint')[0] as HTMLElement;
            this._hintTextRef = document.getElementsByClassName('ac-hint-text')[0] as HTMLElement;
            this._arrowRef = document.getElementsByClassName('ac-hint-arrow')[0] as HTMLElement;
            HintHost._current = this;
            // Any touch dismisses it: it has said its piece, and must not cover what the user reaches for
            fromEvent(document, 'pointerdown', { capture: true })
                .pipe(takeUntil(this._disposed$))
                .subscribe(() => this.hide());
        } catch (error) {
            errorLog?.log(`constructor: unhandled error:`, error);
            this.dispose();
        }
    }

    public dispose(): void {
        if (this._disposed$.closed)
            return;

        this._disposed$.next();
        this._disposed$.complete();
        if (HintHost._current === this)
            HintHost._current = null;
        if (this._shownElement)
            this.hide();
    }

    /** Routed through a static, so the render script - alive for the page's lifetime - never captures an instance. */
    public static request(element: HTMLElement, text: string): void {
        HintHost._current?.setRequest(element, text);
    }

    // Private methods

    /**
     * An empty text clears this element's hint only: every element carrying the attribute reports
     * its empty initial value on mount, which must not cancel what another element is showing.
     */
    private setRequest(element: HTMLElement, text: string): void {
        if (!text) {
            if (this._shownElement === element)
                this.hide();
            return;
        }

        // The same hint can be asked for by several copies of a control (e.g. a header button under a
        // full-screen overlay that has its own); only the one the user can see may anchor it.
        if (!this.canAnchor(element))
            return;

        this.show(element, text);
    }

    private show(element: HTMLElement, text: string): void {
        this.stopPositionUpdate();
        this._shownElement = element;
        this._hintTextRef.textContent = text;
        this._hintRef.classList.add('show');
        const position = element.dataset.hintPosition;
        const placement = (position && position.length > 0 ? position : 'bottom') as Placement;
        this._clearPositionUpdate = autoUpdate(element, this._hintRef, () => {
            if (element.isConnected)
                void BubbleHost.place(element, this._hintRef, this._arrowRef, placement);
            else
                this.hide();
        });
    }

    private hide(): void {
        this.stopPositionUpdate();
        this._shownElement = null;
        this._hintRef.classList.remove('show');
    }

    private stopPositionUpdate(): void {
        this._clearPositionUpdate?.();
        this._clearPositionUpdate = null;
    }

    private canAnchor(element: HTMLElement): boolean {
        if (!element.isConnected)
            return false;

        const rect = element.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0)
            return false;

        const top = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
        return !!top && (element.contains(top) || top.contains(element));
    }
}

// Registered at import time, like every other render script: the attribute can be in the very first
// render, well before the host component mounts.
MutationProcessor.registerRenderScript(HintScript, (element, value) => HintHost.request(element, value));
