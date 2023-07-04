import { Disposable } from 'disposable';
import { Subject, takeUntil, debounceTime, fromEvent } from 'rxjs';
import { Log } from 'logging';

const { debugLog } = Log.get('TextBox');

export class TextBox implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();
    private input: HTMLInputElement;

    public static create(input: HTMLInputElement): TextBox {
        return new TextBox(input);
    }

    constructor(input: HTMLInputElement) {
        this.input = input;
        fromEvent(input, 'input')
            .pipe(
                takeUntil(this.disposed$),
                debounceTime(800),
            )
            .subscribe(() => {
                debugLog?.log(`input handler, value:`, input.value);
                input.dispatchEvent(new Event('change'));
            });
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public focus() {
        this.input.focus();
    }
}
