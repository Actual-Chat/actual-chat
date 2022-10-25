import { Disposable } from 'disposable';
import { Subject, takeUntil, debounceTime, fromEvent } from 'rxjs';
import { Log, LogLevel } from 'logging';

const LogScope = 'TextBox';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class TextBox implements Disposable {
    private disposed$: Subject<void> = new Subject<void>();
    private input: HTMLInputElement;

    public static create(input: HTMLInputElement): TextBox {
        return new TextBox(input);
    }

    constructor(input: HTMLInputElement) {
        this.input = input;
        fromEvent(input, 'input')
            .pipe(takeUntil(this.disposed$))
            .pipe(debounceTime(800))
            .subscribe(() => {
                debugLog?.log(`input handler, value:`, input.value);
                input.dispatchEvent(new Event('change'));
            });
    }

    public focus() {
        this.input.focus();
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }
}
