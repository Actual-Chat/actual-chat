import { Disposable } from 'disposable';
import { Subject, takeUntil, debounceTime, fromEvent } from 'rxjs';
import { getLogs } from 'logging';
import { setupMobileKeyboardHandler } from 'dom-helpers';

const { debugLog } = getLogs('TextBox');

export class TextBox implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();
    private input: HTMLInputElement;
    private blazorRef: DotNet.DotNetObject | null;

    public static create(input: HTMLInputElement, blazorRef?: DotNet.DotNetObject): TextBox {
        return new TextBox(input, blazorRef ?? null);
    }

    constructor(input: HTMLInputElement, blazorRef: DotNet.DotNetObject | null) {
        this.input = input;
        this.blazorRef = blazorRef;
        fromEvent(input, 'input')
            .pipe(
                takeUntil(this.disposed$),
                debounceTime(800),
            )
            .subscribe(() => {
                debugLog?.log(`input handler, value:`, input.value);
                if (this.blazorRef) {
                    void this.blazorRef.invokeMethodAsync('OnDebouncedChange', input.value);
                } else {
                    input.dispatchEvent(new Event('change', { bubbles: true }));
                }
            });

        setupMobileKeyboardHandler(input, this.disposed$);
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.blazorRef = null;
    }

    public focus() {
        this.input.focus({ preventScroll: true });
    }
}
