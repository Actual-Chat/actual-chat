import { fromEvent, Subject, takeUntil, exhaustMap, merge, tap } from 'rxjs';
import { Log, LogLevel, LogScope } from 'logging';
import { stopEvent } from 'event-handling';

const LogScope: LogScope = 'CopyToClipboard';
const errorLog = Log.get(LogScope, LogLevel.Error);

export class CopyToClipboard {
    private readonly inputRef: HTMLInputElement;
    private readonly buttonRef: HTMLButtonElement;
    private disposed$: Subject<void> = new Subject<void>();

    public constructor(inputRef: HTMLInputElement, buttonRef: HTMLButtonElement) {
        this.inputRef = inputRef;
        this.buttonRef = buttonRef;
        merge(fromEvent(this.buttonRef, 'click'), fromEvent(this.inputRef, 'click')).pipe(
            takeUntil(this.disposed$),
            tap(stopEvent),
            exhaustMap(() => this.onClick()),
        ).subscribe();
    }

    public static create(inputRef: HTMLInputElement, buttonRef: HTMLButtonElement) {
        return new CopyToClipboard(inputRef, buttonRef);
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private async onClick() {
        this.inputRef.select();
        const text = this.inputRef.dataset.copyText ?? this.inputRef.value;
        return navigator.clipboard.writeText(text).catch(e => errorLog?.log(`onClick: failed to write to clipboard`, e));
    }
}
